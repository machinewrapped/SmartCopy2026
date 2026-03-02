using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class MoveStep : IPipelineStep
{
    public MoveStep(string destinationPath)
    {
        DestinationPath = destinationPath;
    }

    public string DestinationPath { get; set; }

    public StepKind StepType => StepKind.Move;
    public bool IsExecutable => true;

    public TransformStepConfig Config => new(StepType, new JsonObject { ["destinationPath"] = DestinationPath });

    public void Validate(StepValidationContext context)
    {
        context.ValidateHasSelectedInputs();
        context.ValidateSourceExists("Move");
        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            context.AddBlockingIssue("Step.MissingDestination", "Move requires a destination path.");
        }
        context.SourceExists = false;
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var node in context.GetPreviewSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;
            // Only preview top-level selected nodes (parent not selected) to avoid duplicate entries.
            if (node.Parent is { } p && context.IsPreviewSelected(p)) continue;

            var nodeCtx = context.GetNodeContext(node);
            var targetProvider = nodeCtx.ResolveProvider(DestinationPath)
                ?? throw new InvalidOperationException("TargetProvider must be set for MoveStep.");

            var destination = targetProvider.JoinPath(DestinationPath, nodeCtx.PathSegments);
            var destResult = await targetProvider.ExistsAsync(destination, ct)
                ? DestinationResult.Overwritten
                : DestinationResult.Created;

            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.Moved,
                DestinationPath: destination,
                DestinationResult: destResult,
                NumberOfFilesAffected: node.CountSelectedFiles(),
                NumberOfFoldersAffected: node.CountSelectedFolders(),
                InputBytes: node.Size,
                OutputBytes: node.Size);
        }
    }

    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        var nodeCtx = context.GetNodeContext(context.RootNode);
        var targetProvider = nodeCtx.ResolveProvider(DestinationPath)
            ?? throw new InvalidOperationException("TargetProvider must be set for MoveStep.");
        var sameProvider = ReferenceEquals(targetProvider, context.SourceProvider);
        var canAtomicMove = targetProvider.Capabilities.CanAtomicMove;

        await foreach (var result in WalkAndMoveAsync(context.RootNode, context, targetProvider, sameProvider, canAtomicMove, ct))
            yield return result;
    }

    // Depth-first recursive move: child directories first, then files in the current node.
    // Atomically moves entire subtrees where possible; falls back to piecewise otherwise.
    private async IAsyncEnumerable<TransformResult> WalkAndMoveAsync(
        DirectoryTreeNode node, IStepContext context,
        IFileSystemProvider targetProvider, bool sameProvider, bool canAtomicMove,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var child in node.Children)
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(child)) continue;
            if (child.CheckState == CheckState.Unchecked) continue;

            if (sameProvider && canAtomicMove && CanMoveEntireSubtree(child))
            {
                var childCtx = context.GetNodeContext(child);
                var dest = targetProvider.JoinPath(DestinationPath, childCtx.PathSegments);
                var destExists = await targetProvider.ExistsAsync(dest, ct);

                if (destExists && context.OverwriteMode == OverwriteMode.Skip)
                {
                    yield return new TransformResult(
                        IsSuccess: true,
                        SourceNode: child,
                        SourceNodeResult: SourceResult.None,
                        DestinationPath: dest,
                        InputBytes: child.Size);
                    continue;
                }

                await context.SourceProvider.MoveAsync(child.FullPath, dest, ct);
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: child,
                    SourceNodeResult: SourceResult.Moved,
                    DestinationPath: dest,
                    DestinationResult: destExists ? DestinationResult.Overwritten : DestinationResult.Created,
                    NumberOfFilesAffected: child.CountAllFiles(),
                    NumberOfFoldersAffected: child.CountAllFolders(),
                    InputBytes: child.Size,
                    OutputBytes: child.Size);
            }
            else
            {
                // Atomic move not possible (cross-provider or partial selection): recurse piecewise.
                await foreach (var result in WalkAndMoveAsync(child, context, targetProvider, sameProvider, canAtomicMove, ct))
                    yield return result;
            }
        }

        foreach (var file in node.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (!file.IsSelected || context.IsNodeFailed(file)) continue;

            var fileCtx = context.GetNodeContext(file);
            var fileDest = targetProvider.JoinPath(DestinationPath, fileCtx.PathSegments);
            var fileDestExists = await targetProvider.ExistsAsync(fileDest, ct);

            if (fileDestExists && context.OverwriteMode == OverwriteMode.Skip)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: file,
                    SourceNodeResult: SourceResult.None,
                    DestinationPath: fileDest,
                    InputBytes: file.Size);
                continue;
            }

            if (sameProvider && canAtomicMove)
            {
                await context.SourceProvider.MoveAsync(file.FullPath, fileDest, ct);
            }
            else
            {
                await using var stream = await context.SourceProvider.OpenReadAsync(file.FullPath, ct);
                await targetProvider.WriteAsync(fileDest, stream, progress: null, ct);
                await context.SourceProvider.DeleteAsync(file.FullPath, ct);
            }

            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: file,
                SourceNodeResult: SourceResult.Moved,
                DestinationPath: fileDest,
                DestinationResult: fileDestExists ? DestinationResult.Overwritten : DestinationResult.Created,
                NumberOfFilesAffected: 1,
                InputBytes: file.Size,
                OutputBytes: file.Size);
        }
    }

    /// <summary>
    /// Returns true when an entire directory subtree is fully checked and all files
    /// are filter-included, making it safe to move atomically as a unit.
    /// </summary>
    private static bool CanMoveEntireSubtree(DirectoryTreeNode node)
    {
        if (node.CheckState != CheckState.Checked) return false;
        if (!node.Files.All(f => f.FilterResult == FilterResult.Included)) return false;
        foreach (var child in node.Children)
            if (!CanMoveEntireSubtree(child)) return false;
        return true;
    }
}
