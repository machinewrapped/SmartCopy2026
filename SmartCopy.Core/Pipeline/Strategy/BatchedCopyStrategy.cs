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
        string destPath,
        OverwriteMode mode,
        bool skipExistsCheck,
        SourceResult successResult,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // One pool-rented buffer for the whole selection; reused across flushes, returned on dispose.
        using var buffer = new BatchCopyBuffer(Settings.BatchBufferBytes);

        // Files at/below this size batch; larger ones stream individually. Capped to the buffer
        // capacity, so "size <= ceiling" alone guarantees the file fits the buffer. The 512 KiB
        // default keeps >=2 files per flush on a 1 MiB+ buffer (Docs/optimisation-strategies.md Phase 3).
        var ceiling = Settings.BatchEligibilityCeilingBytes;
        var effectiveCeiling = ceiling <= 0 ? buffer.Capacity : Math.Min(ceiling, buffer.Capacity);

        foreach (var node in EnumerateForBatching(context.RootNode))
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

            // null => already exists and OverwriteMode is Skip; report skipped without reading.
            var destResult = await ResolveDestResultAsync(targetProvider, destination, mode, skipExistsCheck, ct);
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
                if (!buffer.HasSpaceFor(fileSize))
                {
                    await foreach (var r in FlushBatchAsync(buffer, targetProvider, context, successResult, ct))
                        yield return r;
                }

                // Read failures are per-file: mark the node failed and keep batching the rest.
                string? readError = null;
                try
                {
                    await using var src = await context.SourceProvider.OpenReadAsync(node.FullPath, ct);
                    await buffer.AccumulateAsync(src, fileSize, destination, destResult.Value, node, ct);
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
                await foreach (var r in FlushBatchAsync(buffer, targetProvider, context, successResult, ct))
                    yield return r;

                yield return await CopyOneFileAsync(context, node, destination, destResult.Value, targetProvider, successResult, ct);
            }
        }

        // Drain whatever remains after the last file.
        await foreach (var r in FlushBatchAsync(buffer, targetProvider, context, successResult, ct))
            yield return r;
    }

    /// <summary>
    /// Intentional depth-first enumeration for batching: yields each directory's own selected files
    /// smallest-first (so the buffer packs optimally), then recurses into child directories in order —
    /// completing each subtree in full before the next sibling. This deliberate order (rather than
    /// relying on <see cref="DirectoryNode.GetSelectedDescendants"/>'s incidental traversal) gives the
    /// resume property: because accumulation and flush write in this same order, an interrupted copy
    /// leaves a clean depth-first prefix on disk, so completed and missing subtrees are obvious.
    /// <para>
    /// Recursion descends into every child that is not fully Unchecked: a partially-selected directory
    /// (CheckState=Indeterminate) is not itself selected yet can still contain selected descendants that
    /// must be copied — gating recursion on the directory's own <c>IsSelected</c> would silently drop
    /// them. Conversely an Unchecked directory has, by tri-state propagation, no Checked descendant
    /// (<see cref="DirectoryNode.RecalculateCheckState"/>), so its subtree is pruned rather than walked.
    /// The directory node is only yielded as a traversal marker when it is itself selected (the caller
    /// emits a no-op result for markers).
    /// </para>
    /// </summary>
    private static IEnumerable<DirectoryTreeNode> EnumerateForBatching(DirectoryNode dir)
    {
        foreach (var file in dir.Files.Where(f => f.IsSelected).OrderBy(f => f.Size))
            yield return file;

        foreach (var child in dir.Children)
        {
            if (child.CheckState == CheckState.Unchecked)
                continue; // no Checked descendant possible — skip the whole subtree

            if (child.IsSelected)
                yield return child;
            foreach (var node in EnumerateForBatching(child))
                yield return node;
        }
    }

    /// <summary>
    /// The write phase: drains every accumulated entry to the target, one result per file, then
    /// resets the buffer for reuse. Write failures are per-file and do not abort the batch.
    /// </summary>
    private async IAsyncEnumerable<TransformResult> FlushBatchAsync(
        BatchCopyBuffer buffer,
        IFileSystemProvider targetProvider,
        IStepContext context,
        SourceResult successResult,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!buffer.HasEntries)
            yield break;

        foreach (var entry in buffer.Entries)
        {
            IProgress<long>? progress = null;
            if (entry.Length > 0 && context is IFileTransferProgressSink sink)
                progress = new DelegateProgress<long>(b => sink.ReportFileTransferBytes(entry.Node, b, entry.Length));

            string? error = null;
            try
            {
                using var ms = buffer.OpenSegmentStream(entry);
                await targetProvider.WriteAsync(entry.Destination, ms, progress, SettingsFor(entry.Length), ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                context.MarkFailed(entry.Node);
                error = ex.Message;
            }

            yield return error is null
                ? new TransformResult(IsSuccess: true, SourceNode: entry.Node,
                    SourceNodeResult: successResult, DestinationPath: entry.Destination,
                    DestinationResult: entry.DestResult, NumberOfFilesAffected: 1,
                    InputBytes: entry.Length, OutputBytes: entry.Length)
                : new TransformResult(IsSuccess: false, SourceNode: entry.Node,
                    SourceNodeResult: SourceResult.Skipped, ErrorMessage: error);
        }

        buffer.Reset();
    }
}
