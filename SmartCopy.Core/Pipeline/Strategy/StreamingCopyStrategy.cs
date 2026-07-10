using System.Runtime.CompilerServices;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline.Strategy;

/// <summary>
/// Copies files one at a time, interleaving a read and a write per file. The default strategy
/// when batching is disabled (<c>BatchBufferBytes == 0</c>) and the path used for any single
/// out-of-band file transfer (e.g. a move fallback).
/// </summary>
public sealed class StreamingCopyStrategy(OperationalSettings settings, bool targetSupportsStaging)
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
        foreach (var node in context.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;

            // Directories carry no bytes; emit a no-op marker so callers can track traversal.
            if (node.IsDirectory)
            {
                yield return new TransformResult(IsSuccess: true, SourceNode: node, SourceNodeResult: SourceResult.None);
                continue;
            }

            // PathSegments are mutated by earlier steps (flatten/rename), so the destination is
            // computed from the per-node context rather than the source path.
            var nodeCtx = context.GetNodeContext(node);
            var destination = targetProvider.JoinPath(destPath, nodeCtx.PathSegments);

            // null => the file already exists and OverwriteMode is Skip; report it skipped.
            var destResult = await ResolveDestResultAsync(targetSession, destination, mode, ct);
            if (destResult is null)
            {
                yield return SkippedResult(node, destination);
                continue;
            }

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
}
