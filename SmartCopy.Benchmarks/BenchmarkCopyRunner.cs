using System.Diagnostics;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Progress;

namespace SmartCopy.Benchmarks;

internal static class BenchmarkCopyRunner
{
    public static async Task<IReadOnlyList<TransformResult>> RunAsync(
        PipelineJob job,
        string destinationPath,
        OverwriteMode overwriteMode,
        long directWriteThresholdBytes,
        bool skipExistsCheckForOverwrite,
        int copyBufferSizeBytes,
        CancellationToken ct)
    {
        job.RootNode.BuildStats();

        var results = new List<TransformResult>();
        var stopwatch = Stopwatch.StartNew();

        long totalBytes = job.RootNode.TotalSelectedBytes;
        int totalFiles = job.RootNode.NumSelectedFiles;
        long completedBytes = 0;
        int filesCompleted = 0;

        string? lastCreatedDir = null;

        foreach (var node in job.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();

            if (node.IsDirectory)
            {
                results.Add(new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.None));
                continue;
            }

            if (job.PauseToken is not null)
            {
                await job.PauseToken.WaitIfPausedAsync(ct);
            }

            var segments = node.RelativePathSegments.Length > 0
                ? node.RelativePathSegments
                : [node.Name];
            var relativePath = Path.Combine(segments);
            var destination = Path.GetFullPath(Path.Combine(destinationPath, relativePath));

            var destResult = DestinationResult.Written;
            bool skipWrite = false;

            if (!skipExistsCheckForOverwrite || overwriteMode == OverwriteMode.Skip)
            {
                var exists = File.Exists(destination);
                if (exists && overwriteMode == OverwriteMode.Skip)
                {
                    results.Add(new TransformResult(
                        IsSuccess: true,
                        SourceNode: node,
                        SourceNodeResult: SourceResult.Skipped,
                        DestinationPath: destination,
                        NumberOfFilesSkipped: 1,
                        InputBytes: node.Size));
                    skipWrite = true;
                }
                else
                {
                    destResult = exists ? DestinationResult.Overwritten : DestinationResult.Created;
                }
            }

            if (skipWrite)
            {
                continue;
            }

            // Ensure parent directory exists
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory) && directory != lastCreatedDir)
            {
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                lastCreatedDir = directory;
            }

            var startTick = Stopwatch.GetTimestamp();
            bool isDirectWrite = node.Size <= directWriteThresholdBytes;
            Exception? copyError = null;

            try
            {
                if (isDirectWrite)
                {
                    var bytes = await File.ReadAllBytesAsync(node.FullPath, ct);
                    await File.WriteAllBytesAsync(destination, bytes, ct);
                }
                else
                {
                    await using var sourceStream = new FileStream(
                        node.FullPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: copyBufferSizeBytes,
                        useAsync: true);

                    await using var destStream = new FileStream(
                        destination,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        bufferSize: copyBufferSizeBytes,
                        useAsync: true);

                    await sourceStream.CopyToAsync(destStream, copyBufferSizeBytes, ct);
                }
            }
            catch (Exception ex)
            {
                copyError = ex;
                try
                {
                    if (File.Exists(destination))
                    {
                        File.Delete(destination);
                    }
                }
                catch { /* Ignore cleanup failures */ }
            }

            var elapsedTick = Stopwatch.GetTimestamp() - startTick;
            var elapsedDuration = TimeSpan.FromSeconds((double)elapsedTick / Stopwatch.Frequency);

            if (copyError is not null)
            {
                results.Add(new TransformResult(
                    IsSuccess: false,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.None,
                    DestinationPath: destination,
                    ErrorMessage: copyError.Message,
                    ExecutionDuration: elapsedDuration));
            }
            else
            {
                filesCompleted++;
                completedBytes += node.Size;

                var result = new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.Copied,
                    DestinationPath: destination,
                    DestinationResult: destResult,
                    NumberOfFilesAffected: 1,
                    InputBytes: node.Size,
                    OutputBytes: node.Size,
                    ExecutionDuration: elapsedDuration);

                results.Add(result);
                job.NodeProgress?.Report(result);

                var currentElapsed = stopwatch.Elapsed;
                var remaining = EstimateRemaining(currentElapsed, completedBytes, totalBytes);

                job.Progress?.Report(new OperationProgress(
                    CurrentFile: node.CanonicalRelativePath,
                    CurrentFileBytes: node.Size,
                    CurrentFileTotalBytes: node.Size,
                    FilesCompleted: filesCompleted,
                    FilesTotal: totalFiles,
                    TotalBytesCompleted: completedBytes,
                    TotalBytes: totalBytes,
                    Elapsed: currentElapsed,
                    EstimatedRemaining: remaining));
            }
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
}
