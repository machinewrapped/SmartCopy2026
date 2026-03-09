using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class MoveStep : IPipelineStep, IHasDestinationPath, IHasFreeSpaceCheck
{
    public StepKind StepType => StepKind.Move;
    public bool IsExecutable => true;

    public MoveStep(string destinationPath, OverwriteMode overwriteMode = OverwriteMode.Skip)
    {
        DestinationPath = destinationPath;
        OverwriteMode = overwriteMode;
    }

    public OverwriteMode OverwriteMode { get; set; }

    public TransformStepConfig Config => new(StepType, new JsonObject 
    { 
        ["destinationPath"] = DestinationPath,
        ["overwriteMode"] = OverwriteMode.ToString()
    });

    public string AutoSummary => HasDestinationPath ? $"Move to {PathHelper.GetFriendlyTarget(DestinationPath)}" : StepType.ForDisplay();

    public string Description => HasDestinationPath ? $"Move to {DestinationPath} ({OverwriteMode})" : "Destination required";

    private string? _destinationPath;
    public string? DestinationPath
    {
        get => _destinationPath;
        set => _destinationPath = value;
    }

    public bool HasDestinationPath => !string.IsNullOrWhiteSpace(DestinationPath);

    public Task<FreeSpaceValidationResult?> ValidateFreeSpace(
        long bytesNeeded,
        IFileSystemProvider source,
        IPathResolver registry,
        IReadOnlyDictionary<string, long?> freeSpaceCache,
        CancellationToken ct)
    {
        if (bytesNeeded <= 0) return FreeSpaceValidationResult.NullResult;
        if (DestinationPath is null) return FreeSpaceValidationResult.NullResult;

        var target = registry.ResolveProvider(DestinationPath);
        if (target is null) return FreeSpaceValidationResult.NullResult;
        if (target.Capabilities.CanQueryFreeSpace == false) return FreeSpaceValidationResult.NullResult;
        if (!freeSpaceCache.TryGetValue(target.RootPath, out var free) || free is null) return FreeSpaceValidationResult.NullResult;

        // No space consumed by move on the same volume
        if (source.VolumeId is { } vid && target.VolumeId == vid) return FreeSpaceValidationResult.NullResult;

        return FreeSpaceValidationResult.Result(bytesNeeded, free.Value, target.RootPath);
    }

    public async Task Validate(StepValidationContext context, CancellationToken ct = default)
    {
        context.ValidateHasSelectedInputs();
        context.ValidateSourceExists("Move");
        if (!HasDestinationPath)
        {
            context.AddBlockingIssue("Step.MissingDestination", "Move requires a destination path.");
        }
        await context.AddFreeSpaceWarning(this, ct);
        context.SourceExists = false;
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        if (DestinationPath is null)
        {
            yield break;
        }

        // Directories whose destination already exists must be merged, not moved
        // as a unit. Track them so their children are not suppressed by parent-collapsing.
        HashSet<DirectoryTreeNode>? mergeNodes = null;

        foreach (var node in context.GetPreviewSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;

            // Collapse children into parent unless the parent is being merged.
            if (node.Parent is { } p 
                && context.IsPreviewSelected(p)
                && mergeNodes?.Contains(p) != true)
            {
                continue;
            }

            var nodeCtx = context.GetNodeContext(node);
            var targetProvider = nodeCtx.ResolveProvider(DestinationPath)
                ?? throw new InvalidOperationException("TargetProvider must be set for MoveStep.");

            var destination = targetProvider.JoinPath(DestinationPath, nodeCtx.PathSegments);
            var destinationExists = await targetProvider.ExistsAsync(destination, ct);

            // When a directory already exists at the destination we need to merge:
            // skip reporting it as a unit and let its children be previewed individually.
            if (node.IsDirectory && destinationExists)
            {
                (mergeNodes ??= []).Add(node);
                continue;
            }

            if (destinationExists && OverwriteMode == OverwriteMode.Skip)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.Skipped,
                    DestinationPath: destination,
                    NumberOfFilesSkipped: node.CountSelectedFiles(),
                    NumberOfFoldersSkipped: node.CountSelectedFolders(),
                    InputBytes: node.Size);
                continue;
            }

            var destResult = destinationExists
                ? DestinationResult.Overwritten
                : DestinationResult.Created;

            var selectedBytes = GetSelectedFileBytes(node);
            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.Moved,
                DestinationPath: destination,
                DestinationResult: destResult,
                NumberOfFilesAffected: node.CountSelectedFiles(),
                NumberOfFoldersAffected: node.CountSelectedFolders(),
                InputBytes: selectedBytes,
                OutputBytes: selectedBytes);
        }
    }

    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        if (DestinationPath is null)
        {
            throw new InvalidOperationException("DestinationPath must be set for MoveStep.");
        }

        var nodeCtx = context.GetNodeContext(context.RootNode);
        var targetProvider = nodeCtx.ResolveProvider(DestinationPath)
            ?? throw new InvalidOperationException("TargetProvider must be set for MoveStep.");
        var sameVolume = context.SourceProvider.VolumeId is { } vid && targetProvider.VolumeId == vid;
        var canAtomicMove = sameVolume && targetProvider.Capabilities.CanAtomicMove;

        await foreach (var result in WalkAndMoveAsync(context.RootNode, context, DestinationPath, targetProvider, canAtomicMove, OverwriteMode, ct))
            yield return result;
    }

    // Depth-first recursive move: child directories first, then files in the current node.
    // Atomically moves entire subtrees where possible; falls back to piecewise otherwise.
    private static async IAsyncEnumerable<TransformResult> WalkAndMoveAsync(
        DirectoryTreeNode node, IStepContext context,
        string destinationPath,
        IFileSystemProvider targetProvider, bool canAtomicMove,
        OverwriteMode overwriteMode,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var child in node.Children)
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(child)) continue;
            if (child.CheckState == CheckState.Unchecked) continue;

            var childCtx = context.GetNodeContext(child);
            var dest = targetProvider.JoinPath(destinationPath, childCtx.PathSegments);
            var destExists = await targetProvider.ExistsAsync(dest, ct);

            // If the destination already exists we must recurse to merge contents,
            // even when an atomic move would otherwise be possible.
            if (!destExists && canAtomicMove && CanMoveEntireSubtree(child))
            {
                await context.SourceProvider.MoveAsync(child.FullPath, dest, ct);
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: child,
                    SourceNodeResult: SourceResult.Moved,
                    DestinationPath: dest,
                    DestinationResult: DestinationResult.Created,
                    NumberOfFilesAffected: child.CountAllFiles(),
                    NumberOfFoldersAffected: child.CountAllFolders(),
                    InputBytes: child.Size,
                    OutputBytes: child.Size);
            }
            else
            {
                // Destination exists (merge needed), cross-provider, or partial selection: recurse piecewise.
                var allMoved = true;
                await foreach (var result in WalkAndMoveAsync(child, context, destinationPath, targetProvider, canAtomicMove, overwriteMode, ct))
                {
                    if (result.SourceNodeResult == SourceResult.Skipped)
                        allMoved = false;
                    yield return result;
                }

                // Delete the now-empty source directory when the subtree was fully selected and nothing was skipped.
                if (allMoved && !context.IsNodeFailed(child) && CanMoveEntireSubtree(child))
                    await context.SourceProvider.DeleteAsync(child.FullPath, ct);
            }
        }

        foreach (var file in node.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (!file.IsSelected || context.IsNodeFailed(file)) continue;

            var fileCtx = context.GetNodeContext(file);
            var fileDest = targetProvider.JoinPath(destinationPath, fileCtx.PathSegments);
            var fileDestExists = await targetProvider.ExistsAsync(fileDest, ct);

            if (fileDestExists && overwriteMode == OverwriteMode.Skip)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: file,
                    SourceNodeResult: SourceResult.Skipped,
                    DestinationPath: fileDest,
                    NumberOfFilesSkipped: 1,
                    InputBytes: file.Size);
                continue;
            }

            if (canAtomicMove)
            {
                await context.SourceProvider.MoveAsync(file.FullPath, fileDest, ct);
            }
            else
            {
                await using (var stream = await context.SourceProvider.OpenReadAsync(file.FullPath, ct))
                {
                    await targetProvider.WriteAsync(fileDest, stream, progress: null, ct);
                }
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
    /// Recursively sums the sizes of all selected files within <paramref name="node"/>.
    /// Used to report accurate byte counts for directory-level move actions in preview,
    /// since <see cref="DirectoryTreeNode.Size"/> is always 0 for directory nodes.
    /// </summary>
    private static long GetSelectedFileBytes(DirectoryTreeNode node)
    {
        if (!node.IsDirectory)
            return node.IsSelected ? node.Size : 0;
        long total = 0;
        foreach (var file in node.Files)
            if (file.IsSelected) total += file.Size;
        foreach (var child in node.Children)
            total += GetSelectedFileBytes(child);
        return total;
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
