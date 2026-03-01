using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using SmartCopy.Core.DirectoryTree;
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
        var targetProvider = context.TargetProvider
            ?? throw new InvalidOperationException("TargetProvider must be set for MoveStep.");

        foreach (var node in context.GetPreviewSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;
            // Only preview top-level selected nodes (parent not selected) to avoid duplicate entries.
            if (node.Parent is { } p && context.IsPreviewSelected(p)) continue;

            var nodeCtx = context.GetNodeContext(node);
            var destination = StepPathHelper.BuildDestinationPath(DestinationPath, nodeCtx.PathSegments);
            var destResult = await targetProvider.ExistsAsync(
                StepPathHelper.BuildDestinationPath(targetProvider, DestinationPath, nodeCtx.PathSegments), ct)
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
        var targetProvider = context.TargetProvider
            ?? throw new InvalidOperationException("TargetProvider must be set for MoveStep.");

        var sameProvider = ReferenceEquals(targetProvider, context.SourceProvider);
        var canAtomicMove = targetProvider.Capabilities.CanAtomicMove;

        // Nodes covered by an earlier atomic directory move are skipped.
        var handledNodes = new HashSet<DirectoryTreeNode>();

        foreach (var node in context.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;
            if (handledNodes.Contains(node)) continue;

            if (node.IsDirectory)
            {
                if (sameProvider && canAtomicMove && CanMoveEntireSubtree(node))
                {
                    var nodeCtx = context.GetNodeContext(node);
                    var destination = StepPathHelper.BuildDestinationPath(targetProvider, DestinationPath, nodeCtx.PathSegments);
                    var destExists = await targetProvider.ExistsAsync(destination, ct);

                    if (destExists && context.OverwriteMode == OverwriteMode.Skip)
                    {
                        MarkDescendantsHandled(node, handledNodes);
                        yield return new TransformResult(
                            IsSuccess: true,
                            SourceNode: node,
                            SourceNodeResult: SourceResult.None,
                            DestinationPath: destination,
                            InputBytes: node.Size);
                        continue;
                    }

                    await context.SourceProvider.MoveAsync(node.FullPath, destination, ct);
                    MarkDescendantsHandled(node, handledNodes);
                    yield return new TransformResult(
                        IsSuccess: true,
                        SourceNode: node,
                        SourceNodeResult: SourceResult.Moved,
                        DestinationPath: destination,
                        DestinationResult: destExists ? DestinationResult.Overwritten : DestinationResult.Created,
                        NumberOfFilesAffected: node.CountAllFiles(),
                        NumberOfFoldersAffected: node.CountAllFolders(),
                        InputBytes: node.Size,
                        OutputBytes: node.Size);
                }
                else
                {
                    // Directory cannot be moved atomically (cross-provider or partial subtree).
                    context.MarkFailed(node);
                    MarkDescendantsHandled(node, handledNodes);
                    yield return new TransformResult(
                        IsSuccess: false,
                        SourceNode: node,
                        SourceNodeResult: SourceResult.None);
                }
                continue;
            }

            // File node
            var fileCtx = context.GetNodeContext(node);
            var fileDest = StepPathHelper.BuildDestinationPath(targetProvider, DestinationPath, fileCtx.PathSegments);
            var fileDestExists = await targetProvider.ExistsAsync(fileDest, ct);

            if (fileDestExists && context.OverwriteMode == OverwriteMode.Skip)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.None,
                    DestinationPath: fileDest,
                    InputBytes: node.Size);
                continue;
            }

            if (sameProvider && canAtomicMove)
            {
                await context.SourceProvider.MoveAsync(node.FullPath, fileDest, ct);
            }
            else
            {
                await using var stream = await context.SourceProvider.OpenReadAsync(node.FullPath, ct);
                await targetProvider.WriteAsync(fileDest, stream, progress: null, ct);
                await context.SourceProvider.DeleteAsync(node.FullPath, ct);
            }

            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.Moved,
                DestinationPath: fileDest,
                DestinationResult: fileDestExists ? DestinationResult.Overwritten : DestinationResult.Created,
                NumberOfFilesAffected: 1,
                InputBytes: node.Size,
                OutputBytes: node.Size);
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

    private static void MarkDescendantsHandled(DirectoryTreeNode node, HashSet<DirectoryTreeNode> handled)
    {
        foreach (var file in node.Files)
            handled.Add(file);
        foreach (var child in node.Children)
        {
            handled.Add(child);
            MarkDescendantsHandled(child, handled);
        }
    }
}
