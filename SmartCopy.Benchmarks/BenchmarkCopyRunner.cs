using System.Buffers;
using System.Diagnostics;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Progress;

namespace SmartCopy.Benchmarks;

internal static class BenchmarkCopyRunner
{
    private sealed class BatchedFile
    {
        public required DirectoryTreeNode Node { get; init; }
        public required string DestinationPath { get; init; }
        public required DestinationResult DestResult { get; init; }
        public required int Offset { get; init; }
        public required int Length { get; init; }
        public required TimeSpan ReadDuration { get; init; }
    }

    public static async Task<IReadOnlyList<TransformResult>> RunAsync(
        PipelineJob job,
        string destinationPath,
        OverwriteMode overwriteMode,
        long directWriteThresholdBytes,
        long bufferBatchBytes,
        long batchEligibilityThresholdBytes,
        int copyBufferSizeBytes,
        bool batchOrderByFileSize,
        bool enableWriteSequentialScan,
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

        // 1. Process directory nodes first to ensure their entries exist in results
        foreach (var dir in job.RootNode.GetSelectedDescendants().Where(n => n.IsDirectory))
        {
            results.Add(new TransformResult(
                IsSuccess: true,
                SourceNode: dir,
                SourceNodeResult: SourceResult.None));
        }

        // 2. Match the production batch traversal: each directory's files smallest-first,
        // then each child subtree depth-first, preserving directory-cohesive interruption semantics.
        var sortedFileNodes = EnumerateForBatching(job.RootNode, batchOrderByFileSize)
            .Where(n => !n.IsDirectory)
            .ToList();

        var currentBatch = new List<BatchedFile>();
        long currentBatchBytes = 0;
        byte[]? sharedBatchBuffer = null;
        int currentBatchOffset = 0;
        long effectiveEligibilityThreshold = batchEligibilityThresholdBytes == 0 ? bufferBatchBytes : batchEligibilityThresholdBytes;
        var hasUsableBatchBuffer = bufferBatchBytes > 0 && bufferBatchBytes <= int.MaxValue;

        async Task FlushBatchAsync(List<BatchedFile> batch, byte[] sharedBuffer)
        {
            if (batch.Count == 0) return;

            foreach (var batched in batch)
            {
                ct.ThrowIfCancellationRequested();

                if (job.PauseToken is not null)
                {
                    await job.PauseToken.WaitIfPausedAsync(ct);
                }

                var writeStartTick = Stopwatch.GetTimestamp();
                bool isDirectWrite = batched.Node.Size <= directWriteThresholdBytes;
                Exception? copyError = null;
                string? stagedPath = null;

                try
                {
                    // Ensure parent directory exists before writing
                    var directory = Path.GetDirectoryName(batched.DestinationPath);
                    if (!string.IsNullOrEmpty(directory) && directory != lastCreatedDir)
                    {
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        lastCreatedDir = directory;
                    }

                    string targetWritePath = batched.DestinationPath;
                    if (!isDirectWrite)
                    {
                        var dir = Path.GetDirectoryName(batched.DestinationPath) ?? Path.GetTempPath();
                        var fileName = Path.GetFileName(batched.DestinationPath);
                        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "smartcopy" : fileName;
                        stagedPath = Path.Combine(dir, $".{safeFileName}.smartcopy.tmp.{Guid.NewGuid():N}");
                        targetWritePath = stagedPath;
                    }

                    if (isDirectWrite)
                    {
                        // Use sequential FileStream to write rented sliced buffer directly
                        await using var destStream = new FileStream(
                            targetWritePath,
                            new FileStreamOptions
                            {
                                Mode = FileMode.Create,
                                Access = FileAccess.Write,
                                Share = FileShare.None,
                                BufferSize = copyBufferSizeBytes,
                                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                            });

                        await destStream.WriteAsync(sharedBuffer.AsMemory(batched.Offset, batched.Length), ct);
                    }
                    else
                    {
                        var options = FileOptions.Asynchronous;
                        if (enableWriteSequentialScan)
                        {
                            options |= FileOptions.SequentialScan;
                        }

                        await using (var destStream = new FileStream(
                            targetWritePath,
                            new FileStreamOptions
                            {
                                Mode = FileMode.Create,
                                Access = FileAccess.Write,
                                Share = FileShare.None,
                                BufferSize = copyBufferSizeBytes,
                                Options = options
                            }))
                        {
                            await destStream.WriteAsync(sharedBuffer.AsMemory(batched.Offset, batched.Length), ct);
                        }

                        File.Move(stagedPath!, batched.DestinationPath, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    copyError = ex;
                    try
                    {
                        if (stagedPath != null && File.Exists(stagedPath))
                        {
                            File.Delete(stagedPath);
                        }
                        else if (isDirectWrite && File.Exists(batched.DestinationPath))
                        {
                            File.Delete(batched.DestinationPath);
                        }
                    }
                    catch { /* Ignore cleanup failures */ }
                }

                var writeTicks = Stopwatch.GetTimestamp() - writeStartTick;
                var writeDuration = TimeSpan.FromSeconds((double)writeTicks / Stopwatch.Frequency);
                var elapsedDuration = batched.ReadDuration + writeDuration;

                if (copyError is not null)
                {
                    results.Add(new TransformResult(
                        IsSuccess: false,
                        SourceNode: batched.Node,
                        SourceNodeResult: SourceResult.None,
                        DestinationPath: batched.DestinationPath,
                        ErrorMessage: copyError.Message,
                        ExecutionDuration: elapsedDuration));
                }
                else
                {
                    filesCompleted++;
                    completedBytes += batched.Node.Size;

                    var result = new TransformResult(
                        IsSuccess: true,
                        SourceNode: batched.Node,
                        SourceNodeResult: SourceResult.Copied,
                        DestinationPath: batched.DestinationPath,
                        DestinationResult: batched.DestResult,
                        NumberOfFilesAffected: 1,
                        InputBytes: batched.Node.Size,
                        OutputBytes: batched.Node.Size,
                        ExecutionDuration: elapsedDuration);

                    results.Add(result);
                    job.NodeProgress?.Report(result);

                    var currentElapsed = stopwatch.Elapsed;
                    var remaining = EstimateRemaining(currentElapsed, completedBytes, totalBytes);

                    job.Progress?.Report(new OperationProgress(
                        CurrentFile: batched.Node.CanonicalRelativePath,
                        CurrentFileBytes: batched.Node.Size,
                        CurrentFileTotalBytes: batched.Node.Size,
                        FilesCompleted: filesCompleted,
                        FilesTotal: totalFiles,
                        TotalBytesCompleted: completedBytes,
                        TotalBytes: totalBytes,
                        Elapsed: currentElapsed,
                        EstimatedRemaining: remaining));
                }
            }

            batch.Clear();
        }

        try
        {
            foreach (var node in sortedFileNodes)
            {
            ct.ThrowIfCancellationRequested();

            if (job.PauseToken is not null)
            {
                await job.PauseToken.WaitIfPausedAsync(ct);
            }

            var segments = node.RelativePathSegments.Length > 0
                ? node.RelativePathSegments
                : [node.Name];
            var relativePath = Path.Combine(segments);
            var destination = Path.GetFullPath(Path.Combine(destinationPath, relativePath));

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
                continue;
            }

            var destResult = exists ? DestinationResult.Overwritten : DestinationResult.Created;

            var isBatchable =
                hasUsableBatchBuffer &&
                node.Size <= bufferBatchBytes &&
                node.Size <= effectiveEligibilityThreshold;

            if (isBatchable)
            {
                if (currentBatch.Count > 0 && currentBatchBytes + node.Size > bufferBatchBytes)
                {
                    await FlushBatchAsync(currentBatch, sharedBatchBuffer!);
                    ArrayPool<byte>.Shared.Return(sharedBatchBuffer!);
                    sharedBatchBuffer = null;
                    currentBatchBytes = 0;
                    currentBatchOffset = 0;
                }

                sharedBatchBuffer ??= ArrayPool<byte>.Shared.Rent((int)bufferBatchBytes);

                var readStartTick = Stopwatch.GetTimestamp();
                int bytesRead = 0;

                try
                {
                    await using (var sourceStream = new FileStream(
                        node.FullPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: copyBufferSizeBytes,
                        useAsync: true))
                    {
                        // Read exactly the file size into our shared pool buffer
                        while (bytesRead < node.Size)
                        {
                            int read = await sourceStream.ReadAsync(sharedBatchBuffer, currentBatchOffset + bytesRead, (int)node.Size - bytesRead, ct);
                            if (read == 0) break;
                            bytesRead += read;
                        }
                    }
                }
                catch (Exception ex)
                {
                    var readTicks = Stopwatch.GetTimestamp() - readStartTick;
                    var readDuration = TimeSpan.FromSeconds((double)readTicks / Stopwatch.Frequency);

                    results.Add(new TransformResult(
                        IsSuccess: false,
                        SourceNode: node,
                        SourceNodeResult: SourceResult.None,
                        DestinationPath: destination,
                        ErrorMessage: ex.Message,
                        ExecutionDuration: readDuration));
                    continue;
                }

                var readTicksCompleted = Stopwatch.GetTimestamp() - readStartTick;
                var readDurationCompleted = TimeSpan.FromSeconds((double)readTicksCompleted / Stopwatch.Frequency);

                currentBatch.Add(new BatchedFile
                {
                    Node = node,
                    DestinationPath = destination,
                    DestResult = destResult,
                    Offset = currentBatchOffset,
                    Length = bytesRead,
                    ReadDuration = readDurationCompleted
                });
                currentBatchBytes += node.Size;
                currentBatchOffset += bytesRead;
            }
            else
            {
                // Not batchable: flush any pending batch first
                if (currentBatch.Count > 0)
                {
                    await FlushBatchAsync(currentBatch, sharedBatchBuffer!);
                    ArrayPool<byte>.Shared.Return(sharedBatchBuffer!);
                    sharedBatchBuffer = null;
                    currentBatchBytes = 0;
                    currentBatchOffset = 0;
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
                string? stagedPath = null;

                try
                {
                    string targetWritePath = destination;
                    if (!isDirectWrite)
                    {
                        var dir = Path.GetDirectoryName(destination) ?? Path.GetTempPath();
                        var fileName = Path.GetFileName(destination);
                        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "smartcopy" : fileName;
                        stagedPath = Path.Combine(dir, $".{safeFileName}.smartcopy.tmp.{Guid.NewGuid():N}");
                        targetWritePath = stagedPath;
                    }

                    if (isDirectWrite)
                    {
                        var bytes = await File.ReadAllBytesAsync(node.FullPath, ct);
                        await File.WriteAllBytesAsync(targetWritePath, bytes, ct);
                    }
                    else
                    {
                        var writeOptions = FileOptions.Asynchronous;
                        if (enableWriteSequentialScan)
                        {
                            writeOptions |= FileOptions.SequentialScan;
                        }

                        await using (var sourceStream = new FileStream(
                            node.FullPath,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            bufferSize: copyBufferSizeBytes,
                            useAsync: true))
                        {
                            await using (var destStream = new FileStream(
                                targetWritePath,
                                new FileStreamOptions
                                {
                                    Mode = FileMode.Create,
                                    Access = FileAccess.Write,
                                    Share = FileShare.None,
                                    BufferSize = copyBufferSizeBytes,
                                    Options = writeOptions
                                }))
                            {
                                await sourceStream.CopyToAsync(destStream, copyBufferSizeBytes, ct);
                            }
                        }

                        File.Move(stagedPath!, destination, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    copyError = ex;
                    try
                    {
                        if (stagedPath != null && File.Exists(stagedPath))
                        {
                            File.Delete(stagedPath);
                        }
                        else if (isDirectWrite && File.Exists(destination))
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
            }

            // Flush any remaining batched files at the end.
            if (currentBatch.Count > 0)
            {
                await FlushBatchAsync(currentBatch, sharedBatchBuffer!);
            }

            return results;
        }
        finally
        {
            if (sharedBatchBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(sharedBatchBuffer);
            }
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

    private static IEnumerable<DirectoryTreeNode> EnumerateForBatching(DirectoryNode dir, bool orderFilesBySize)
    {
        var files = dir.Files.Where(f => f.IsSelected);
        if (orderFilesBySize)
            files = files.OrderBy(f => f.Size);

        foreach (var file in files)
            yield return file;

        foreach (var child in dir.Children)
        {
            if (child.CheckState == CheckState.Unchecked ||
                child.FilterResult == FilterResult.Excluded ||
                child.IsMarkedForRemoval)
                continue;

            if (child.IsSelected)
                yield return child;

            foreach (var node in EnumerateForBatching(child, orderFilesBySize))
                yield return node;
        }
    }
}
