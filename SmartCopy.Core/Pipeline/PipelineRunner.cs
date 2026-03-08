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

    public PipelineRunner(TransformPipeline pipeline)
    {
        _pipeline = pipeline;
        _pipeline.Validate();
    }

    public async Task<OperationPlan> PreviewAsync(
        PipelineJob job,
        CancellationToken ct = default)
    {
        _pipeline.Validate(new PipelineValidationContext(
            job.RootNode.GetSelectedDescendants().Any()));

        var context = new StepContext(job);
        var actions = new List<PlannedAction>();

        foreach (var step in _pipeline.Steps)
        {
            ct.ThrowIfCancellationRequested();

            await foreach (var result in step.PreviewAsync(context, ct))
            {
                if (result.IsSuccess && result.SourceNodeResult != SourceResult.None)
                {
                    actions.Add(new PlannedAction(
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
            }
        }

        _previewCompleted = true;

        var warnings = new List<string>();
        foreach (var step in _pipeline.Steps)
        {
            if (step is DeleteStep ds && ds.Mode == DeleteMode.Trash && !job.SourceProvider.Capabilities.CanTrash)
            {
                warnings.Add("Trash not available for this path — files will be permanently deleted");
            }

            if (step is MoveStep ms && ms.HasDestinationPath)
            {
                var targetProvider = job.ProviderRegistry.ResolveProvider(ms.DestinationPath!);
                var sameVolume = job.SourceProvider.VolumeId is { } vid && targetProvider?.VolumeId == vid;
                if (!sameVolume)
                {
                    warnings.Add("Destination is on another drive, atomic move is not possible");
                }
            }
        }

        return new OperationPlan
        {
            Actions = actions,
            TotalInputBytes = actions.Sum(a => a.InputBytes),
            TotalEstimatedOutputBytes = actions.Sum(a => a.OutputBytes),
            Warnings = warnings,
        };
    }

    public async Task<IReadOnlyList<TransformResult>> ExecuteAsync(PipelineJob job)
    {
        _pipeline.Validate(new PipelineValidationContext(
            job.RootNode.GetSelectedDescendants().Any()));

        if (_pipeline.HasDeleteStep && !_previewCompleted)
        {
            throw new InvalidOperationException(
                "Pipelines containing a DeleteStep must be previewed before execution.");
        }

        var context = new StepContext(job);
        var results = new List<TransformResult>();

        var stopwatch = Stopwatch.StartNew();
        long totalBytes = GetAllSelectedBytes(job.RootNode);
        int totalFiles = job.RootNode.CountSelectedFiles();
        long completedBytes = 0;
        int filesCompleted = 0;
        int stepIndex = 0;

        foreach (var step in _pipeline.Steps)
        {
            job.CancellationToken.ThrowIfCancellationRequested();
            job.StepStarted?.Invoke(stepIndex);

            await foreach (var result in step.ApplyAsync(context, job.CancellationToken))
            {
                if (job.PauseToken is not null)
                    await job.PauseToken.WaitIfPausedAsync(job.CancellationToken);

                results.Add(result);
                job.NodeProgress?.Report(result);

                if (!result.IsSuccess || result.SourceNodeResult == SourceResult.None)
                    continue;

                if (step.IsExecutable)
                {
                    filesCompleted += result.NumberOfFilesAffected;
                    completedBytes += result.InputBytes;
                    var elapsed = stopwatch.Elapsed;
                    var remaining = EstimateRemaining(elapsed, completedBytes, totalBytes);

                    job.Progress?.Report(new OperationProgress(
                        CurrentFile: result.SourceNode.CanonicalRelativePath,
                        CurrentFileBytes: result.InputBytes,
                        CurrentFileTotalBytes: result.InputBytes,
                        FilesCompleted: filesCompleted,
                        FilesTotal: totalFiles,
                        TotalBytesCompleted: completedBytes,
                        TotalBytes: totalBytes,
                        Elapsed: elapsed,
                        EstimatedRemaining: remaining));
                }
            }

            stepIndex++;
        }

        return results;
    }

    private static long GetAllSelectedBytes(DirectoryTreeNode node)
    {
        long total = 0;
        foreach (var file in node.Files)
            if (file.IsSelected) total += file.Size;
        foreach (var child in node.Children)
            total += GetAllSelectedBytes(child);
        return total;
    }

    private static TimeSpan EstimateRemaining(TimeSpan elapsed, long completed, long total)
    {
        if (completed <= 0 || elapsed.TotalSeconds <= 0 || completed >= total)
            return TimeSpan.Zero;

        var rate = completed / elapsed.TotalSeconds;
        if (rate <= 0)
            return TimeSpan.Zero;

        return TimeSpan.FromSeconds((total - completed) / rate);
    }

    private sealed class StepContext : IStepContext
    {
        private readonly Dictionary<DirectoryTreeNode, PipelineContext> _contexts = new();
        private readonly HashSet<DirectoryTreeNode> _failedNodes = new();

        public DirectoryTreeNode RootNode { get; }
        public IFileSystemProvider SourceProvider { get; }
        public FileSystemProviderRegistry ProviderRegistry { get; }
        public bool ShowHiddenFiles { get; }
        public bool AllowDeleteReadOnly { get; }
        public ITrashService TrashService { get; }

        public StepContext(PipelineJob job)
        {
            RootNode = job.RootNode;
            SourceProvider = job.SourceProvider;
            ProviderRegistry = job.ProviderRegistry;
            ShowHiddenFiles = job.ShowHiddenFiles;
            AllowDeleteReadOnly = job.AllowDeleteReadOnly;
            TrashService = job.TrashService;
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
    }
}
