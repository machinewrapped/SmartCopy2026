using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Progress;

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
        IEnumerable<FileSystemNode> selectedNodes,
        IFileSystemProvider sourceProvider,
        IFileSystemProvider? targetProvider,
        OverwriteMode overwriteMode,
        DeleteMode deleteMode,
        CancellationToken ct = default)
    {
        var actions = new List<PlannedAction>();
        var selected = selectedNodes.ToList();

        foreach (var node in selected)
        {
            ct.ThrowIfCancellationRequested();
            var context = CreateContext(node, sourceProvider, targetProvider, overwriteMode, deleteMode);
            foreach (var step in _pipeline.Steps)
            {
                var preview = step.Preview(context);

                // Propagate the transformed path so downstream steps (e.g. CopyStep after
                // FlattenStep) preview against the correct path, matching what execution produces.
                if (step.IsPathStep && !string.IsNullOrWhiteSpace(preview.DestinationPath))
                {
                    context.CurrentPath = preview.DestinationPath;
                }

                if (!string.IsNullOrWhiteSpace(preview.DestinationPath))
                {
                    var warning = await GetWarningAsync(preview.DestinationPath, targetProvider, ct);
                    actions.Add(new PlannedAction(
                        StepSummary: step.StepType.ToString(),
                        SourcePath: node.FullPath,
                        DestinationPath: preview.DestinationPath!,
                        InputBytes: node.Size,
                        EstimatedOutputBytes: preview.OutputBytes == 0 ? node.Size : preview.OutputBytes,
                        Warning: warning));
                }

                if (!preview.Success)
                {
                    break;
                }
            }
        }

        _previewCompleted = true;
        return new OperationPlan
        {
            Actions = actions,
            TotalInputBytes = selected.Sum(node => node.Size),
            TotalEstimatedOutputBytes = actions.Sum(action => action.EstimatedOutputBytes),
        };
    }

    public async Task<IReadOnlyList<TransformResult>> ExecuteAsync(
        IEnumerable<FileSystemNode> selectedNodes,
        IFileSystemProvider sourceProvider,
        IFileSystemProvider? targetProvider,
        OverwriteMode overwriteMode,
        DeleteMode deleteMode,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (_pipeline.HasDeleteStep && !_previewCompleted)
        {
            throw new InvalidOperationException(
                "Pipelines containing a DeleteStep must be previewed before execution.");
        }

        var selected = selectedNodes.ToList();
        var results = new List<TransformResult>();
        var stopwatch = Stopwatch.StartNew();
        long totalBytes = selected.Sum(node => node.Size);
        long completedBytes = 0;
        int filesCompleted = 0;

        foreach (var node in selected)
        {
            ct.ThrowIfCancellationRequested();

            var context = CreateContext(node, sourceProvider, targetProvider, overwriteMode, deleteMode);
            foreach (var step in _pipeline.Steps)
            {
                ct.ThrowIfCancellationRequested();
                var result = await step.ApplyAsync(context, ct);
                results.Add(result);
                if (!result.Success)
                {
                    break;
                }
            }

            filesCompleted++;
            completedBytes += node.Size;
            var elapsed = stopwatch.Elapsed;
            var remaining = EstimateRemaining(elapsed, completedBytes, totalBytes);

            progress?.Report(new OperationProgress(
                CurrentFile: node.FullPath,
                CurrentFileBytes: node.Size,
                CurrentFileTotalBytes: node.Size,
                FilesCompleted: filesCompleted,
                FilesTotal: selected.Count,
                TotalBytesCompleted: completedBytes,
                TotalBytes: totalBytes,
                Elapsed: elapsed,
                EstimatedRemaining: remaining));
        }

        return results;
    }

    private static TransformContext CreateContext(
        FileSystemNode node,
        IFileSystemProvider sourceProvider,
        IFileSystemProvider? targetProvider,
        OverwriteMode overwriteMode,
        DeleteMode deleteMode)
    {
        var extension = Path.GetExtension(node.Name).TrimStart('.');
        return new TransformContext
        {
            SourceNode = node,
            SourceProvider = sourceProvider,
            TargetProvider = targetProvider,
            CurrentPath = string.IsNullOrWhiteSpace(node.RelativePath) ? node.Name : node.RelativePath,
            CurrentExtension = extension,
            OverwriteMode = overwriteMode,
            DeleteMode = deleteMode,
        };
    }

    private static async Task<PlanWarning?> GetWarningAsync(
        string destinationPath,
        IFileSystemProvider? targetProvider,
        CancellationToken ct)
    {
        if (targetProvider is null)
        {
            return null;
        }

        var exists = await targetProvider.ExistsAsync(destinationPath, ct);
        return exists ? PlanWarning.DestinationExists : null;
    }

    private static TimeSpan EstimateRemaining(TimeSpan elapsed, long completed, long total)
    {
        if (completed <= 0 || elapsed.TotalSeconds <= 0 || completed >= total)
        {
            return TimeSpan.Zero;
        }

        var rate = completed / elapsed.TotalSeconds;
        if (rate <= 0)
        {
            return TimeSpan.Zero;
        }

        var seconds = (total - completed) / rate;
        return TimeSpan.FromSeconds(seconds);
    }
}
