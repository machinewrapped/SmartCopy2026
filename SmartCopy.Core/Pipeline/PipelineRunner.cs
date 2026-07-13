using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.FileSystem.Hardware;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Pipeline.Strategy;
using SmartCopy.Core.Pipeline.Validation;
using SmartCopy.Core.Trash;

namespace SmartCopy.Core.Pipeline;

public sealed partial class PipelineRunner
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

        var strategyNotes = await BuildStrategyNotesAsync(job, context, ct);

        // The optimisations status only means anything when something actually transfers bytes
        // (a Copy or a non-atomic Move) — i.e. exactly when strategy notes were produced.
        var strategyStatus = strategyNotes.Count > 0 ? DescribeOptimisations(job.OperationalSettings) : "";

        return new OperationPlan
        {
            Actions = actions,
            TotalInputBytes = actions.Sum(a => a.InputBytes),
            TotalEstimatedOutputBytes = actions.Sum(a => a.OutputBytes),
            Warnings = warnings,
            InfoMessages = infoMessages,
            Errors = errors,
            StrategyNotes = strategyNotes,
            StrategyStatus = strategyStatus,
        };
    }

    /// <summary>
    /// Resolves, without transferring any bytes, the copy strategy each executable destination step
    /// (Copy/Move) will use, and formats a one-line summary per step for the preview pane. The
    /// classification probes are registry-cached and cheap. This is purely informational: a probe that
    /// throws is swallowed so a strategy-description failure can never block or break a preview.
    /// </summary>
    private async Task<IReadOnlyList<string>> BuildStrategyNotesAsync(
        PipelineJob job, IStepContext context, CancellationToken ct)
    {
        var notes = new List<string>();
        foreach (var step in _pipeline.Steps)
        {
            if (!step.IsExecutable || step is not IHasDestinationPath { DestinationPath: { } destPath })
                continue;

            try
            {
                var target = job.ProviderRegistry.ResolveProvider(destPath);
                if (target is null) continue;

                var source = await job.SourceProvider.GetClassificationAsync(ct);
                var dest = await target.GetClassificationAsync(ct);
                var sameVolume = job.SourceProvider.VolumeId is { } vid && target.VolumeId == vid;

                // A same-volume atomic Move is a rename — no copy strategy ever runs, so there are no
                // transfer mechanics to report. Only Copy and non-atomic (copy-then-delete) Move qualify.
                if (step is MoveStep && sameVolume && target.Capabilities.CanAtomicMove)
                    continue;

                var strategy = await context.ResolveCopyStrategyAsync(target, ct);
                // Description (full destination path + overwrite mode) rather than AutoSummary (leaf
                // folder only) so steps targeting like-named folders on different drives stay distinct.
                notes.Add($"{step.Description} — {DescribeDrivePair(source, dest, sameVolume)}: {strategy.Describe()}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Informational only — never let a classification/strategy probe block the preview.
                // Drive classification can throw provider- or platform-specific exceptions beyond IO,
                // so swallow everything except cancellation (which must propagate).
            }
        }

        return notes;
    }

    /// <summary>Drive-pair label for a strategy note, e.g. "SSD (NVMe)→HDD (SATA), same volume".</summary>
    private static string DescribeDrivePair(DriveClassification source, DriveClassification target, bool sameVolume)
    {
        var pair = $"{source}→{target}";
        return sameVolume ? $"{pair}, same volume" : pair;
    }

    /// <summary>Plan-wide copy-optimisations status. Destination routing is the proxy the UI toggles
    /// with "Allow Copy Optimisations": on ⇒ buffer chosen per drive pair; off ⇒ the fixed default.</summary>
    private static string DescribeOptimisations(OperationalSettings settings) =>
        settings.DestinationRoutingEnabled
            ? "Copy optimisations: on — buffer routed by drive pair"
            : "Copy optimisations: off — fixed buffer, no routing";

    public async Task<IReadOnlyList<TransformResult>> ExecuteAsync(PipelineJob job, CancellationToken ct = default)
    {
        await PrepareExecutionAsync(job, ct);

        if (job.SourceProvider is IDeleteOperationProvider deleteOperationProvider)
            deleteOperationProvider.BeginDeleteOperation();

        var results = new List<TransformResult>();
        var progress = new ExecutionProgressReporter(job);
        int stepIndex = 0;
        var context = new StepContext(job, progress.ReportTransferBytes);

        foreach (var step in _pipeline.Steps)
        {
            job.CancellationToken.ThrowIfCancellationRequested();
            job.StepStarted?.Invoke(stepIndex);

            if (step.IsExecutable)
            {
                progress.BeginExecutableStep();
            }

            var lastResultElapsed = progress.Elapsed;
            await foreach (var rawResult in step.ApplyAsync(context, job.CancellationToken))
            {
                var currentElapsed = progress.Elapsed;
                var perResultDuration = currentElapsed - lastResultElapsed;
                var result = rawResult.ExecutionDuration is null
                    ? rawResult with { ExecutionDuration = perResultDuration }
                    : rawResult;

                if (job.PauseToken is not null)
                    await job.PauseToken.WaitIfPausedAsync(job.CancellationToken);

                results.Add(result);
                job.NodeProgress?.Report(result);

                progress.CompleteResult(result, step.IsExecutable, currentElapsed);

                lastResultElapsed = currentElapsed;
            }

            stepIndex++;
        }

        return results;
    }

    private async Task PrepareExecutionAsync(PipelineJob job, CancellationToken ct)
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
        public OperationalSettings OperationalSettings { get; }
        public ICopyStrategyPolicy CopyStrategyPolicy { get; }

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
            OperationalSettings = job.OperationalSettings;
            CopyStrategyPolicy = job.CopyStrategyPolicy;
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
