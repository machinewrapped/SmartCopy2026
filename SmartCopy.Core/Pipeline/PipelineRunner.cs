using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Validation;
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
        PipelineJob job,
        CancellationToken ct = default)
    {
        _pipeline.Validate(new PipelineValidationContext(job.SelectedFiles.Count > 0));

        var actions = new List<PlannedAction>();
        var contexts = new Dictionary<FileSystemNode, TransformContext>();
        TransformContext GetOrCreate(FileSystemNode node) =>
            contexts.TryGetValue(node, out var ctx) ? ctx
            : contexts[node] = CreateContext(node, job);

        var workingSet = job.SelectedFiles.ToList();
        var failedNodes = new HashSet<FileSystemNode>();

        foreach (var step in _pipeline.Steps)
        {
            ct.ThrowIfCancellationRequested();

            if (step.ProvidesInput)
            {
                foreach (var node in job.FilterIncludedFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    step.Preview(GetOrCreate(node));
                }
                workingSet = job.FilterIncludedFiles
                    .Where(n => n.CheckState == CheckState.Checked)
                    .ToList();
            }
            else
            {
                foreach (var node in workingSet)
                {
                    if (failedNodes.Contains(node)) continue;
                    ct.ThrowIfCancellationRequested();

                    var preview = step.Preview(GetOrCreate(node));

                    if (!string.IsNullOrWhiteSpace(preview.DestinationPath))
                    {
                        var warning = await GetWarningAsync(preview.DestinationPath, job.TargetProvider, ct);
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
                        failedNodes.Add(node);
                    }
                }
            }
        }

        _previewCompleted = true;
        return new OperationPlan
        {
            Actions = actions,
            TotalInputBytes = actions.Sum(a => a.InputBytes),
            TotalEstimatedOutputBytes = actions.Sum(a => a.EstimatedOutputBytes),
        };
    }

    public async Task<IReadOnlyList<TransformResult>> ExecuteAsync(
        PipelineJob job,
        IProgress<OperationProgress>? progress = null,
        CancellationToken ct = default)
    {
        _pipeline.Validate(new PipelineValidationContext(job.SelectedFiles.Count > 0));

        if (_pipeline.HasDeleteStep && !_previewCompleted)
        {
            throw new InvalidOperationException(
                "Pipelines containing a DeleteStep must be previewed before execution.");
        }

        var results = new List<TransformResult>();
        var contexts = new Dictionary<FileSystemNode, TransformContext>();
        TransformContext GetOrCreate(FileSystemNode node) =>
            contexts.TryGetValue(node, out var ctx) ? ctx
            : contexts[node] = CreateContext(node, job);

        var workingSet = job.SelectedFiles.ToList();
        var failedNodes = new HashSet<FileSystemNode>();

        var stopwatch = Stopwatch.StartNew();
        long totalBytes = job.FilterIncludedFiles.Sum(n => n.Size);
        long completedBytes = 0;
        int filesCompleted = 0;

        foreach (var step in _pipeline.Steps)
        {
            ct.ThrowIfCancellationRequested();

            if (step.ProvidesInput)
            {
                foreach (var node in job.FilterIncludedFiles)
                {
                    ct.ThrowIfCancellationRequested();
                    var result = await step.ApplyAsync(GetOrCreate(node), ct);
                    results.Add(result);
                }
                workingSet = job.FilterIncludedFiles
                    .Where(n => n.CheckState == CheckState.Checked)
                    .ToList();
            }
            else
            {
                foreach (var node in workingSet)
                {
                    if (failedNodes.Contains(node)) continue;
                    ct.ThrowIfCancellationRequested();

                    var result = await step.ApplyAsync(GetOrCreate(node), ct);
                    results.Add(result);

                    if (!result.Success)
                    {
                        failedNodes.Add(node);
                    }

                    if (step.IsExecutable)
                    {
                        filesCompleted++;
                        completedBytes += node.Size;
                        var elapsed = stopwatch.Elapsed;
                        var remaining = EstimateRemaining(elapsed, completedBytes, totalBytes);

                        progress?.Report(new OperationProgress(
                            CurrentFile: node.FullPath,
                            CurrentFileBytes: node.Size,
                            CurrentFileTotalBytes: node.Size,
                            FilesCompleted: filesCompleted,
                            FilesTotal: workingSet.Count,
                            TotalBytesCompleted: completedBytes,
                            TotalBytes: totalBytes,
                            Elapsed: elapsed,
                            EstimatedRemaining: remaining));
                    }
                }
            }
        }

        return results;
    }

    private static TransformContext CreateContext(FileSystemNode node, PipelineJob job)
    {
        var extension = Path.GetExtension(node.Name).TrimStart('.');
        var segments = node.RelativePathSegments.Length > 0
            ? node.RelativePathSegments
            : [node.Name];
        return new TransformContext
        {
            SourceNode = node,
            SourceProvider = job.SourceProvider,
            TargetProvider = job.TargetProvider,
            PathSegments = segments,
            CurrentExtension = extension,
            OverwriteMode = job.OverwriteMode,
            DeleteMode = job.DeleteMode,
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
