using System.Diagnostics;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Pipeline.Validation;
using SmartCopy.Core.Progress;
using SmartCopy.Core.Trash;

namespace SmartCopy.Core.Pipeline;

public sealed class PipelineRunner
{
    private readonly TransformPipeline _pipeline;

    // Tracks that PreviewAsync was called before ExecuteAsync on delete pipelines.
    private bool _previewCompleted;

    private FreeSpaceCache _freeSpaceCache = new();

    public PipelineRunner(TransformPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    public async Task<OperationPlan> PreviewAsync(
        PipelineJob job,
        CancellationToken ct = default)
    {
        job.RootNode.BuildStats();

        _freeSpaceCache = await FreeSpaceCache.BuildForPipelineAsync(_pipeline.Steps, job.ProviderRegistry, ct);

        await _pipeline.ValidateAsync(new PipelineValidationContext(
            job.SourceProvider,
            job.ProviderRegistry,
            _freeSpaceCache,
            job.RootNode.TotalSelectedBytes,
            job.RootNode.NumSelectedFiles,
            job.RootNode.NumFilterIncludedFiles,
            job.RootNode.TotalFilterIncludedBytes));

        var context = new StepContext(job);
        var actions = new List<PlannedAction>();
        var warnings = new List<string>();
        var infoMessages = new List<string>();
        var errors = new List<string>();

        foreach (var step in _pipeline.Steps)
        {
            ct.ThrowIfCancellationRequested();
            var stepActions = new List<PlannedAction>();

            await foreach (var result in step.PreviewAsync(context, ct))
            {
                if (result.IsSuccess && result.SourceNodeResult != SourceResult.None)
                {
                    stepActions.Add(new PlannedAction(
                        SourcePath: result.SourceNode.CanonicalRelativePath,
                        SourceResult: result.SourceNodeResult,
                        DestinationPath: result.DestinationPath,
                        DestinationResult: result.DestinationResult,
                        NumberOfFilesAffected: result.NumberOfFilesAffected,
                        NumberOfFoldersAffected: result.NumberOfFoldersAffected,
                        InputBytes: result.InputBytes,
                        OutputBytes: result.OutputBytes,
                        NumberOfFilesSkipped: result.NumberOfFilesSkipped,
                        NumberOfFoldersSkipped: result.NumberOfFoldersSkipped));
                }

                if (result.ActionSummary is not null)
                    infoMessages.Add(result.ActionSummary);

                if (!result.IsSuccess && result.ErrorMessage is { Length: > 0 } errMsg)
                    errors.Add(errMsg);
            }

            if (step is IHasDestinationPath destination)
            {
                await _freeSpaceCache.CacheForDestinationAsync(
                    destination, 
                    job.ProviderRegistry, 
                    ct);
            }

            if (step is IHasFreeSpaceCheck fsCheck)
            {
                long needed = stepActions.Sum(a => a.OutputBytes);
                var fsResult = fsCheck.ValidateFreeSpace(needed, job.SourceProvider, job.ProviderRegistry, _freeSpaceCache);
                if (fsResult != null)
                {
                    if (fsResult.IsViolation)
                        warnings.Add(fsResult.LongMessage);

                    // Update free space cache
                    _freeSpaceCache.ReduceForPath(job.ProviderRegistry, fsResult.TargetRootPath, fsResult.NeededBytes);
                }
            }

            actions.AddRange(stepActions);
        }

        _previewCompleted = true;

        foreach (var step in _pipeline.Steps)
        {
            if (step is DeleteStep ds && ds.Mode == DeleteMode.Trash && !job.SourceProvider.Capabilities.CanTrash)
            {
                warnings.Add("Trash not available for this path — files will be permanently deleted");
            }
        }

        return new OperationPlan
        {
            Actions = actions,
            TotalInputBytes = actions.Sum(a => a.InputBytes),
            TotalEstimatedOutputBytes = actions.Sum(a => a.OutputBytes),
            Warnings = warnings,
            InfoMessages = infoMessages,
            Errors = errors,
        };
    }

    public async Task<IReadOnlyList<TransformResult>> ExecuteAsync(PipelineJob job, CancellationToken ct = default)
    {
        // Make sure selection stats are up to date
        job.RootNode.BuildStats();

        _freeSpaceCache = await FreeSpaceCache.BuildForPipelineAsync(_pipeline.Steps, job.ProviderRegistry, ct);

        // Check that the pipeline is still valid
        await _pipeline.ValidateAsync(new PipelineValidationContext(
            job.SourceProvider,
            job.ProviderRegistry,
            _freeSpaceCache,
            job.RootNode.TotalSelectedBytes,
            job.RootNode.NumSelectedFiles,
            job.RootNode.NumFilterIncludedFiles,
            job.RootNode.TotalFilterIncludedBytes));

        // TODO: if there is a free space issue, fire off a preview

        // TODO: this does not respect the user setting
        if (_pipeline.HasDeleteStep && !_previewCompleted)
        {
            throw new InvalidOperationException(
                "Pipelines containing a DeleteStep must be previewed before execution.");
        }

        var results = new List<TransformResult>();

        var stopwatch = Stopwatch.StartNew();
        long totalBytes = 0;
        int totalFiles = 0;
        long completedBytes = 0;
        int filesCompleted = 0;
        DirectoryTreeNode? inFlightNode = null;
        long inFlightNodeBytes = 0;
        long inFlightNodeTotalBytes = 0;
        var inFlightProgressMinIntervalTicks = Stopwatch.Frequency / 10; // ~10 Hz
        long lastInFlightProgressReportTick = 0;
        int stepIndex = 0;
        var context = new StepContext(job, ReportTransferBytes);

        void ReportTransferBytes(DirectoryTreeNode node, long bytesDelta, long fileTotalBytes)
        {
            if (totalBytes <= 0)
            {
                return;
            }

            if (!ReferenceEquals(inFlightNode, node))
            {
                inFlightNode = node;
                inFlightNodeBytes = 0;
                inFlightNodeTotalBytes = Math.Max(0, fileTotalBytes);
                lastInFlightProgressReportTick = 0;
            }

            if (bytesDelta <= 0)
            {
                return;
            }

            inFlightNodeBytes += bytesDelta;
            if (inFlightNodeTotalBytes > 0 && inFlightNodeBytes > inFlightNodeTotalBytes)
            {
                inFlightNodeBytes = inFlightNodeTotalBytes;
            }

            var completedWithTransfer = completedBytes + inFlightNodeBytes;
            if (completedWithTransfer > totalBytes)
            {
                completedWithTransfer = totalBytes;
            }

            var elapsed = stopwatch.Elapsed;
            var remaining = EstimateRemaining(elapsed, completedWithTransfer, totalBytes);
            var nowTick = Stopwatch.GetTimestamp();
            var fileTransferComplete = inFlightNodeTotalBytes > 0 && inFlightNodeBytes >= inFlightNodeTotalBytes;
            if (!fileTransferComplete &&
                lastInFlightProgressReportTick != 0 &&
                nowTick - lastInFlightProgressReportTick < inFlightProgressMinIntervalTicks)
            {
                return;
            }

            lastInFlightProgressReportTick = nowTick;
            job.Progress?.Report(new OperationProgress(
                CurrentFile: node.CanonicalRelativePath,
                CurrentFileBytes: inFlightNodeBytes,
                CurrentFileTotalBytes: inFlightNodeTotalBytes,
                FilesCompleted: filesCompleted,
                FilesTotal: totalFiles,
                TotalBytesCompleted: completedWithTransfer,
                TotalBytes: totalBytes,
                Elapsed: elapsed,
                EstimatedRemaining: remaining));
        }

        foreach (var step in _pipeline.Steps)
        {
            job.CancellationToken.ThrowIfCancellationRequested();
            job.StepStarted?.Invoke(stepIndex);

            if (step.IsExecutable)
            {
                totalBytes = job.RootNode.TotalSelectedBytes;
                totalFiles = job.RootNode.NumSelectedFiles;
                completedBytes = 0;
                filesCompleted = 0;
                inFlightNode = null;
                inFlightNodeBytes = 0;
                inFlightNodeTotalBytes = 0;
                lastInFlightProgressReportTick = 0;
                stopwatch.Restart();
            }

            var lastResultElapsed = stopwatch.Elapsed;
            await foreach (var rawResult in step.ApplyAsync(context, job.CancellationToken))
            {
                var currentElapsed = stopwatch.Elapsed;
                var perResultDuration = currentElapsed - lastResultElapsed;
                var result = rawResult.ExecutionDuration is null
                    ? rawResult with { ExecutionDuration = perResultDuration }
                    : rawResult;

                if (job.PauseToken is not null)
                    await job.PauseToken.WaitIfPausedAsync(job.CancellationToken);

                results.Add(result);
                job.NodeProgress?.Report(result);

                if (ReferenceEquals(inFlightNode, result.SourceNode))
                {
                    inFlightNode = null;
                    inFlightNodeBytes = 0;
                    inFlightNodeTotalBytes = 0;
                    lastInFlightProgressReportTick = 0;
                }

                if (result.IsSuccess && result.SourceNodeResult != SourceResult.None && step.IsExecutable)
                {
                    filesCompleted += result.NumberOfFilesAffected;
                    completedBytes += result.InputBytes;
                    var remaining = EstimateRemaining(currentElapsed, completedBytes, totalBytes);

                    job.Progress?.Report(new OperationProgress(
                        CurrentFile: result.SourceNode.CanonicalRelativePath,
                        CurrentFileBytes: result.InputBytes,
                        CurrentFileTotalBytes: result.InputBytes,
                        FilesCompleted: filesCompleted,
                        FilesTotal: totalFiles,
                        TotalBytesCompleted: completedBytes,
                        TotalBytes: totalBytes,
                        Elapsed: currentElapsed,
                        EstimatedRemaining: remaining));
                }

                lastResultElapsed = currentElapsed;
            }

            stepIndex++;
        }

        return results;
    }

    private static TimeSpan EstimateRemaining(TimeSpan elapsed, long completed, long total)
    {
        if (completed <= 0 || elapsed.TotalSeconds <= 0 || completed >= total)
            return TimeSpan.Zero;

        var rate = completed / elapsed.TotalSeconds;
        if (rate <= 0)
            return TimeSpan.Zero;

        var remainingSeconds = (total - completed) / rate;
        if (double.IsInfinity(remainingSeconds) || remainingSeconds > TimeSpan.MaxValue.TotalSeconds)
            return TimeSpan.MaxValue;

        return TimeSpan.FromSeconds(remainingSeconds);
    }

    private sealed class StepContext : IStepContext, IFileTransferProgressSink
    {
        private readonly Dictionary<DirectoryTreeNode, PipelineContext> _contexts = new();
        private readonly HashSet<DirectoryTreeNode> _failedNodes = new();
        private readonly Action<DirectoryTreeNode, long, long>? _fileTransferProgress;

        public DirectoryNode RootNode { get; }
        public IFileSystemProvider SourceProvider { get; }
        public FileSystemProviderRegistry ProviderRegistry { get; }
        public bool ShowHiddenFiles { get; }
        public bool AllowDeleteReadOnly { get; }
        public ITrashService TrashService { get; }

        public StepContext(
            PipelineJob job,
            Action<DirectoryTreeNode, long, long>? fileTransferProgress = null)
        {
            RootNode = job.RootNode;
            SourceProvider = job.SourceProvider;
            ProviderRegistry = job.ProviderRegistry;
            ShowHiddenFiles = job.ShowHiddenFiles;
            AllowDeleteReadOnly = job.AllowDeleteReadOnly;
            TrashService = job.TrashService;
            _fileTransferProgress = fileTransferProgress;
        }

        public PipelineContext GetNodeContext(DirectoryTreeNode node)
        {
            if (_contexts.TryGetValue(node, out var context))
                return context;

            var extension = Path.GetExtension(node.Name).TrimStart('.');
            var segments = node.RelativePathSegments.Length > 0
                ? node.RelativePathSegments
                : [node.Name];

            context = new PipelineContext
            {
                SourceNode = node,
                SourceProvider = SourceProvider,
                ProviderRegistry = ProviderRegistry,
                PathSegments = segments,
                CurrentExtension = extension,
                VirtualCheckState = node.CheckState,
            };
            _contexts[node] = context;
            return context;
        }

        public bool IsNodeFailed(DirectoryTreeNode node) => _failedNodes.Contains(node);
        public void MarkFailed(DirectoryTreeNode node) => _failedNodes.Add(node);
        public void ReportFileTransferBytes(DirectoryTreeNode node, long bytesDelta, long fileTotalBytes) =>
            _fileTransferProgress?.Invoke(node, bytesDelta, fileTotalBytes);
    }
}
