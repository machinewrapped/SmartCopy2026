using System.Diagnostics;
using System.Runtime.CompilerServices;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Progress;

namespace SmartCopy.Core.Pipeline.Strategy;

/// <summary>
/// Accumulates small files into a pool-rented buffer during a read phase, then drains it during a
/// write phase — phase-separated I/O that avoids per-file read/write interleaving. Files larger
/// than the batch buffer bypass it and stream individually. Selected when
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

        foreach (var node in EnumerateForBatching(context.RootNode, Settings.BatchOrderByFileSize))
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;

            // Directories carry no bytes; emit a no-op marker so callers can track traversal.
            if (node.IsDirectory)
            {
                yield return new TransformResult(IsSuccess: true, SourceNode: node, SourceNodeResult: SourceResult.None);
                continue;
            }

            var nodeCtx = context.GetNodeContext(node);
            var destination = targetProvider.JoinPath(destPath, nodeCtx.PathSegments);

            // Time the destination check (ExistsAsync): the streaming path's per-file cadence includes this
            // ceremony, so batched timing must bank it too — otherwise bucket comparisons charge the
            // streaming control for a stat the batched variants hide. Banked with the read at flush.
            var ceremonyStart = Stopwatch.GetTimestamp();
            var destResult = await ResolveDestResultAsync(targetSession, destination, mode, ct);
            var ceremonyElapsed = Stopwatch.GetElapsedTime(ceremonyStart);

            // null => already exists and OverwriteMode is Skip; report skipped without reading.
            if (destResult is null)
            {
                yield return SkippedResult(node, destination);
                continue;
            }

            if (node.Size <= effectiveCeiling)
            {
                // Batch-eligible: read it into the buffer now, defer the write to the next flush.
                var fileSize = (int)node.Size;

                // No room left for this file — drain what we have, then start filling again.
                if (!buffer.HasCapacityFor(fileSize))
                {
                    await foreach (var r in FlushBatchAsync(
                        buffer,
                        targetSession,
                        context,
                        successResult,
                        ct))
                        yield return r;
                }

                // Read failures are per-file: mark the node failed and keep batching the rest.
                string? readError = null;
                var readStart = Stopwatch.GetTimestamp();
                try
                {
                    // No bufferSize: the batch reader buffers into its own pooled buffer, so the
                    // source stream is opened unbuffered (avoids a redundant per-file FileStream buffer).
                    await using var src = await context.SourceProvider.OpenReadAsync(node.FullPath, ct: ct);
                    await buffer.AccumulateAsync(
                        src,
                        fileSize,
                        destination,
                        destResult.Value,
                        node,
                        ceremonyElapsed,
                        readStart,
                        ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    context.MarkFailed(node);
                    readError = ex.Message;
                }

                if (readError is not null)
                {
                    yield return new TransformResult(IsSuccess: false, SourceNode: node,
                        SourceNodeResult: SourceResult.Skipped, ErrorMessage: readError);
                }
            }
            else
            {
                // File above the eligibility ceiling — flush pending entries (to preserve order), then
                // stream it directly via the ManualLoop fallback.
                await foreach (var r in FlushBatchAsync(
                    buffer,
                    targetSession,
                    context,
                    successResult,
                    ct))
                    yield return r;

                yield return await CopyOneFileAsync(
                    context,
                    node,
                    destination,
                    destResult.Value,
                    targetSession,
                    successResult,
                    ct);
            }
        }

        // Drain whatever remains after the last file.
        await foreach (var r in FlushBatchAsync(buffer, targetSession, context, successResult, ct))
            yield return r;
    }

    /// <summary>
    /// Intentional depth-first enumeration for batching: each directory's own selected files are
    /// optionally smallest-first (optimal buffer packing), then each child subtree completed in full before the
    /// next sibling (the resume property — accumulation and flush share this order). Recurses into
    /// partially-selected directories but prunes subtrees that can hold no selected node. The traversal,
    /// selection, and prune-polarity rules are documented in <c>Docs/Architecture.md</c> §2.4.1
    /// ("Traversal selection &amp; pruning").
    /// </summary>
    private static IEnumerable<DirectoryTreeNode> EnumerateForBatching(DirectoryNode dir, bool orderFilesBySize)
    {
        var files = dir.Files.Where(f => f.IsSelected);
        if (orderFilesBySize)
            files = files.OrderBy(f => f.Size);

        foreach (var file in files)
            yield return file;

        foreach (var child in dir.Children)
        {
            // Prune subtrees that can hold no selected node — each condition holds for the whole subtree
            // (bottom-up CheckState/FilterResult; recursive MarkForRemoval). See Architecture.md §2.4.1.
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

    /// <summary>
    /// The write phase: drains every accumulated entry to the target, then attributes per-file
    /// <see cref="TransformResult.ExecutionDuration"/> as exact destination-check + read time plus
    /// that entry's measured flush write time. Write failures are per-file and do not abort the batch.
    /// </summary>
    private async IAsyncEnumerable<TransformResult> FlushBatchAsync(
        BatchCopyBuffer buffer,
        IBulkWriteSession targetSession,
        IStepContext context,
        SourceResult successResult,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!buffer.HasEntries)
            yield break;

        var outcomes = new List<(BatchCopyBuffer.Entry Entry, TimeSpan WriteElapsed, string? Error)>(buffer.Entries.Count);
        foreach (var entry in buffer.Entries)
        {
            IProgress<long>? progress = null;
            if (entry.Length > 0 && context is IFileTransferProgressSink sink)
                progress = new DelegateProgress<long>(b => sink.ReportFileTransferBytes(entry.Node, b, entry.Length));

            var writeStart = Stopwatch.GetTimestamp();
            string? error = null;
            try
            {
                using var ms = buffer.OpenSegmentStream(entry);
                await targetSession.WriteAsync(entry.Destination, ms, progress, SettingsFor(entry.Length), ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                context.MarkFailed(entry.Node);
                error = ex.Message;
            }

            outcomes.Add((entry, Stopwatch.GetElapsedTime(writeStart), error));
        }

        foreach (var (entry, writeElapsed, error) in outcomes)
        {
            var duration = entry.PreWriteElapsed + writeElapsed;
            yield return error is null
                ? new TransformResult(IsSuccess: true, SourceNode: entry.Node,
                    SourceNodeResult: successResult, DestinationPath: entry.Destination,
                    DestinationResult: entry.DestResult, NumberOfFilesAffected: 1,
                    InputBytes: entry.Length, OutputBytes: entry.Length, ExecutionDuration: duration)
                : new TransformResult(IsSuccess: false, SourceNode: entry.Node,
                    SourceNodeResult: SourceResult.Skipped, ErrorMessage: error, ExecutionDuration: duration);
        }

        buffer.Reset();
    }
}
