using System.Diagnostics;
using System.Runtime.CompilerServices;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Progress;

namespace SmartCopy.Core.Pipeline.Strategy;

/// <summary>
/// Accumulates small files into a pool-rented buffer during a read phase, then drains it during a
/// write phase — phase-separated I/O that avoids per-file read/write interleaving. Files above
/// the batch-eligibility ceiling bypass it and stream individually. Selected when
/// <c>BatchBufferBytes &gt; 0</c>.
/// </summary>
public sealed class BatchedCopyStrategy(OperationalSettings settings, bool targetSupportsStaging)
    : CopyStrategyBase(settings, targetSupportsStaging)
{
    public override async IAsyncEnumerable<TransformResult> CopySelectionAsync(
        IStepContext context,
        IFileSystemProvider targetProvider,
        IBulkWriteSession targetSession,
        string destPath,
        OverwriteMode mode,
        SourceResult successResult,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // One pool-rented buffer for the whole selection; reused across flushes, returned on dispose.
        using var buffer = new BatchCopyBuffer(Settings.BatchBufferBytes);

        // Files at/below this size batch; larger ones stream individually. Capped to the buffer
        // capacity, so "size <= ceiling" alone guarantees the file fits the buffer. The 512 KiB
        // default keeps >=2 files per flush on a 1 MiB+ buffer, so phase separation actually happens.
        var ceiling = Settings.BatchEligibilityCeilingBytes;
        var effectiveCeiling = ceiling <= 0 ? buffer.Capacity : Math.Min(ceiling, buffer.Capacity);
        var operation = new BatchCopyOperation(
            buffer, effectiveCeiling, context, targetProvider, targetSession, destPath, mode, successResult);

        await foreach (var result in CopyDirectoryAsync(context.RootNode, operation, ct))
            yield return result;

        // Drain whatever remains after the last file.
        await foreach (var r in FlushBatchAsync(operation, ct))
            yield return r;
    }

    private async IAsyncEnumerable<TransformResult> CopyDirectoryAsync(
        DirectoryNode dir, BatchCopyOperation operation, [EnumeratorCancellation] CancellationToken ct)
    {
        var files = dir.Files.Where(f => f.IsSelected);
        if (Settings.BatchTraversalOrder == BatchTraversalOrder.AscendingFileSize)
            files = files.OrderBy(f => f.Size);

        foreach (var file in files)
        {
            await foreach (var result in CopyFileAsync(file, operation, ct))
                yield return result;
        }

        foreach (var child in dir.Children)
        {
            if (child.CheckState == CheckState.Unchecked || child.FilterResult == FilterResult.Excluded || child.IsMarkedForRemoval)
                continue;

            if (child.IsSelected)
                yield return new TransformResult(IsSuccess: true, SourceNode: child, SourceNodeResult: SourceResult.None);

            await foreach (var result in CopyDirectoryAsync(child, operation, ct))
                yield return result;

            if (Settings.BatchFlushPolicy == BatchFlushPolicy.FlushOnCapacityOrDirectoryExit)
            {
                await foreach (var result in FlushBatchAsync(operation, ct))
                    yield return result;
            }
        }
    }

    private async IAsyncEnumerable<TransformResult> CopyFileAsync(
        DirectoryTreeNode node, BatchCopyOperation operation, [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (operation.Context.IsNodeFailed(node)) yield break;

        var destination = operation.TargetProvider.JoinPath(
            operation.DestinationPath, operation.Context.GetNodeContext(node).PathSegments);
        var ceremonyStart = Stopwatch.GetTimestamp();
        var destResult = await ResolveDestResultAsync(operation.TargetSession, destination, operation.Mode, ct);
        var ceremonyElapsed = Stopwatch.GetElapsedTime(ceremonyStart);
        if (destResult is null)
        {
            yield return SkippedResult(node, destination);
            yield break;
        }

        if (node.Size > operation.EffectiveCeiling)
        {
            if (Settings.BatchFlushPolicy == BatchFlushPolicy.FlushBeforeIneligibleFile)
            {
                await foreach (var result in FlushBatchAsync(operation, ct))
                    yield return result;
            }

            yield return await CopyOneFileAsync(operation.Context, node, destination, destResult.Value,
                operation.TargetSession, operation.SuccessResult, ct);
            yield break;
        }

        var fileSize = (int)node.Size;
        if (!operation.Buffer.HasCapacityFor(fileSize))
        {
            await foreach (var result in FlushBatchAsync(operation, ct))
                yield return result;
        }

        string? readError = null;
        var readStart = Stopwatch.GetTimestamp();
        try
        {
            // No bufferSize: the batch reader buffers into its own pooled buffer, so the source
            // stream is opened unbuffered (avoids a redundant per-file FileStream buffer).
            await using var source = await operation.Context.SourceProvider.OpenReadAsync(node.FullPath, ct: ct);
            await operation.Buffer.AccumulateAsync(source, fileSize, destination, destResult.Value, node,
                ceremonyElapsed, readStart, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            operation.Context.MarkFailed(node);
            readError = ex.Message;
        }

        if (readError is not null)
            yield return new TransformResult(IsSuccess: false, SourceNode: node,
                SourceNodeResult: SourceResult.Skipped, ErrorMessage: readError);
    }

    /// <summary>
    /// The write phase: drains every accumulated entry to the target, then attributes per-file
    /// <see cref="TransformResult.ExecutionDuration"/> as exact destination-check + read time plus
    /// that entry's measured flush write time. Write failures are per-file and do not abort the batch.
    /// </summary>
    private async IAsyncEnumerable<TransformResult> FlushBatchAsync(
        BatchCopyOperation operation,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = operation.Buffer;
        if (!buffer.HasEntries)
            yield break;

        foreach (var entry in buffer.Entries)
        {
            IProgress<long>? progress = null;
            if (entry.Length > 0 && operation.Context is IFileTransferProgressSink sink)
                progress = new DelegateProgress<long>(b => sink.ReportFileTransferBytes(entry.Node, b, entry.Length));

            var writeStart = Stopwatch.GetTimestamp();
            string? error = null;
            try
            {
                using var ms = buffer.OpenSegmentStream(entry);
                await operation.TargetSession.WriteAsync(entry.Destination, ms, progress, SettingsFor(entry.Length), ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                operation.Context.MarkFailed(entry.Node);
                error = ex.Message;
            }

            var duration = entry.PreWriteElapsed + Stopwatch.GetElapsedTime(writeStart);
            yield return error is null
                ? new TransformResult(IsSuccess: true, SourceNode: entry.Node,
                    SourceNodeResult: operation.SuccessResult, DestinationPath: entry.Destination,
                    DestinationResult: entry.DestResult, NumberOfFilesAffected: 1,
                    InputBytes: entry.Length, OutputBytes: entry.Length, ExecutionDuration: duration)
                : new TransformResult(IsSuccess: false, SourceNode: entry.Node,
                    SourceNodeResult: SourceResult.Skipped, ErrorMessage: error, ExecutionDuration: duration);
        }

        buffer.Reset();
    }

    private sealed class BatchCopyOperation(
        BatchCopyBuffer buffer,
        long effectiveCeiling,
        IStepContext context,
        IFileSystemProvider targetProvider,
        IBulkWriteSession targetSession,
        string destinationPath,
        OverwriteMode mode,
        SourceResult successResult)
    {
        public BatchCopyBuffer Buffer { get; } = buffer;
        public long EffectiveCeiling { get; } = effectiveCeiling;
        public IStepContext Context { get; } = context;
        public IFileSystemProvider TargetProvider { get; } = targetProvider;
        public IBulkWriteSession TargetSession { get; } = targetSession;
        public string DestinationPath { get; } = destinationPath;
        public OverwriteMode Mode { get; } = mode;
        public SourceResult SuccessResult { get; } = successResult;
    }
}
