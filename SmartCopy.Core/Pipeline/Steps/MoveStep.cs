using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Strategy;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

/// <summary>
/// Executable pipeline step that moves the selected files into a destination tree.
/// <para>
/// A move is a rename when source and destination share a volume and the provider supports atomic
/// moves; otherwise it degrades to copy-then-delete. Both modes are handled by a single depth-first
/// walk (<see cref="WalkAndMoveAsync"/>): it moves whole subtrees in one operation where it safely
/// can, and falls back to per-file work (and source cleanup) where it cannot. The byte transfer in
/// the fallback is delegated to the shared <see cref="Strategy.ICopyStrategy"/>, so move and copy use
/// identical transfer mechanics.
/// </para>
/// </summary>
public sealed class MoveStep : IPipelineStep, IHasDestinationPath, IHasFreeSpaceCheck
{
    public StepKind StepType => StepKind.Move;
    public bool IsExecutable => true;

    public MoveStep(string destinationPath, OverwriteMode overwriteMode = OverwriteMode.Skip)
    {
        DestinationPath = destinationPath;
        OverwriteMode = overwriteMode;
    }

    /// <summary>How to treat a file that already exists at the destination (Skip / Overwrite).</summary>
    public OverwriteMode OverwriteMode { get; set; }

    /// <summary>Rebuilds a step from its serialized config (workflow presets, .sc2 pipelines).</summary>
    internal static MoveStep FromConfig(TransformStepConfig config) =>
        new(config.GetRequired("destinationPath"),
            config.ParseEnum("overwriteMode", OverwriteMode.Skip));

    /// <summary>Serializes this step's destination and overwrite mode for persistence.</summary>
    public TransformStepConfig Config => new(StepType, new JsonObject
    {
        ["destinationPath"] = DestinationPath,
        ["overwriteMode"] = OverwriteMode.ToString()
    });

    public string AutoSummary => HasDestinationPath ? $"Move to {PathHelper.GetFriendlyTarget(DestinationPath)}" : StepType.ForDisplay();

    public string Description => HasDestinationPath ? $"Move to {DestinationPath} ({OverwriteMode})" : "Destination required";

    private string? _destinationPath;

    /// <summary>Root path of the destination tree; files land at <c>DestinationPath + relativeSegments</c>.</summary>
    public string? DestinationPath
    {
        get => _destinationPath;
        set => _destinationPath = value;
    }

    public bool HasDestinationPath => !string.IsNullOrWhiteSpace(DestinationPath);

    /// <summary>
    /// Pre-execution free-space check. A same-volume move consumes no additional space (it is a
    /// rename), so it returns null there; otherwise it validates against the cached destination free
    /// space. Null means "no warning" (nothing to move, no destination, or free space unknown).
    /// </summary>
    public FreeSpaceValidationResult? ValidateFreeSpace(
        long bytesNeeded,
        IFileSystemProvider source,
        IPathResolver registry,
        FreeSpaceCache freeSpaceCache)
    {
        if (bytesNeeded <= 0) return null;
        if (DestinationPath is null) return null;

        var target = registry.ResolveProvider(DestinationPath);
        if (target is null) return null;

        // No space consumed by move on the same volume (it is a rename, not a copy).
        if (source.VolumeId is { } vid && target.VolumeId == vid) return null;

        var cachedFreeSpace = freeSpaceCache.GetForProvider(target);
        if (cachedFreeSpace is null) return null;

        return new FreeSpaceValidationResult(bytesNeeded, cachedFreeSpace.Value, target.RootPath);
    }

    /// <summary>
    /// Static validation run before execution. Beyond the usual selected-input/destination checks, a
    /// move <em>consumes</em> its inputs: it zeroes the selection counters and clears
    /// <see cref="StepValidationContext.SourceExists"/> so a later step in the same pipeline cannot
    /// reference files this move will have relocated.
    /// </summary>
    public Task Validate(StepValidationContext context, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        context.ValidateHasSelectedInputs();
        context.ValidateSourceExists("Move");
        if (!HasDestinationPath)
        {
            context.AddBlockingIssue("Step.MissingDestination", "Move requires a destination path.");
        }
        context.AddFreeSpaceWarning(this);

        // A move removes the selection from the source, so downstream validation must see it gone.
        context.NumFilterIncludedFiles   -= context.SelectedFileCount;
        context.TotalFilterIncludedBytes -= context.SelectedBytes;
        context.SelectedFileCount         = 0;
        context.SelectedBytes             = 0;
        context.SourceExists              = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Produces the planned actions for the preview pane without moving anything. Collapses each
    /// fully-selected subtree to a single row (the unit move), but expands directories whose
    /// destination already exists so the per-file merge is shown accurately.
    /// </summary>
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

            // Collapse children into parent unless the parent is being merged — a fully-selected
            // subtree is reported as one move row, mirroring the atomic subtree move at execution.
            if (node.Parent is { } p
                && context.IsPreviewSelected(p)
                && mergeNodes?.Contains(p) != true)
            {
                continue;
            }

            // PathSegments may have been rewritten by earlier steps, so resolve per node.
            var nodeCtx = context.GetNodeContext(node);
            var targetProvider = nodeCtx.ResolveProvider(DestinationPath)
                ?? throw new InvalidOperationException("TargetProvider must be set for MoveStep.");

            var destination = targetProvider.JoinPath(DestinationPath, nodeCtx.PathSegments);
            var destinationExists = await targetProvider.ExistsAsync(destination, ct);

            // When a directory already exists at the destination we need to merge:
            // skip reporting it as a unit and let its children be previewed individually.
            if (node is DirectoryNode && destinationExists)
            {
                (mergeNodes ??= []).Add(node);
                continue;
            }

            // Pre-existing destination under Skip: the node stays put — preview it as skipped.
            if (destinationExists && OverwriteMode == OverwriteMode.Skip)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.Skipped,
                    DestinationPath: destination,
                    NumberOfFilesSkipped: PipelineHelpers.GetSelectedFileCount(node),
                    NumberOfFoldersSkipped: PipelineHelpers.GetSelectedFolderCount(node),
                    InputBytes: node.Size);
                continue;
            }

            var destResult = destinationExists
                ? DestinationResult.Overwritten
                : DestinationResult.Created;

            // Directory rows report the whole subtree's selected bytes; Size is 0 for directories.
            var selectedBytes = node switch
            {
                DirectoryNode dn => dn.TotalSelectedBytes,
                _ => node.Size
            };

            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.Moved,
                DestinationPath: destination,
                DestinationResult: destResult,
                NumberOfFilesAffected: PipelineHelpers.GetSelectedFileCount(node),
                NumberOfFoldersAffected: PipelineHelpers.GetSelectedFolderCount(node),
                InputBytes: selectedBytes,
                OutputBytes: selectedBytes);
        }
    }

    /// <summary>
    /// Executes the move. Determines once whether an atomic (rename) move is possible for this
    /// source→destination pair, resolves the shared copy strategy for the non-atomic fallback, then
    /// hands off to the recursive walk.
    /// </summary>
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

        // An atomic move (single rename) is only valid within one volume and only if the provider
        // supports it; cross-volume or incapable pairs go through copy-then-delete instead.
        var sameVolume = context.SourceProvider.VolumeId is { } vid && targetProvider.VolumeId == vid;
        var canAtomicMove = sameVolume && targetProvider.Capabilities.CanAtomicMove;

        // Bulk-write scope lets providers establish protocol-level transfer sessions (e.g. MTP).
        await using var _ = targetProvider.BeginBulkWriteAsync();

        // Non-atomic moves degrade to copy+delete; the copy reuses the shared strategy.
        var strategy = await context.ResolveCopyStrategyAsync(targetProvider, ct);

        await foreach (var result in WalkAndMoveAsync(context.RootNode, context, DestinationPath, targetProvider, strategy, canAtomicMove, OverwriteMode, ct))
            yield return result;
    }

    /// <summary>
    /// Depth-first recursive move: child directories first, then files in the current node.
    /// Atomically moves entire subtrees where possible; falls back to piecewise otherwise.
    /// </summary>
    private static async IAsyncEnumerable<TransformResult> WalkAndMoveAsync(
        DirectoryNode node, IStepContext context,
        string destinationPath,
        IFileSystemProvider targetProvider, ICopyStrategy strategy, bool canAtomicMove,
        OverwriteMode overwriteMode,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // --- Directories: try to move each fully-selected subtree as a unit, else recurse. -------
        foreach (var child in node.Children)
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(child)) continue;
            if (child.CheckState == CheckState.Unchecked) continue;

            var childCtx = context.GetNodeContext(child);
            var dest = targetProvider.JoinPath(destinationPath, childCtx.PathSegments);
            var destExists = await targetProvider.ExistsAsync(dest, ct);

            // The atomic fast-path requires three things: a free destination (an existing one must be
            // merged content-by-content), an atomic-capable same-volume pair, and a fully-selected
            // subtree (a partial selection must leave the unselected files behind).
            bool atomicMoved = false;
            if (!destExists && canAtomicMove && CanMoveEntireSubtree(child))
            {
                try
                {
                    await context.SourceProvider.MoveAsync(child.FullPath, dest, ct);
                    atomicMoved = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Atomic move failed; fall back to piecewise walk below.
                    _ = ex;
                }
            }

            if (atomicMoved)
            {
                // One result aggregates the whole subtree that was renamed in a single operation.
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
                // Track whether every descendant moved so we know if the source directory can be removed.
                var allMoved = true;
                await foreach (var result in WalkAndMoveAsync(child, context, destinationPath, targetProvider, strategy, canAtomicMove, overwriteMode, ct))
                {
                    if (!result.IsSuccess || result.SourceNodeResult == SourceResult.Skipped)
                        allMoved = false;
                    yield return result;
                }

                // Delete the now-empty source directory when the subtree was fully selected and nothing was skipped.
                // (A partial selection or a skip leaves files behind, so the directory must remain.)
                if (allMoved && !context.IsNodeFailed(child) && CanMoveEntireSubtree(child))
                {
                    string? dirCleanupError = null;
                    try
                    {
                        await context.SourceProvider.DeleteAsync(child.FullPath, ct);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        dirCleanupError = ex.Message;
                    }

                    // The files moved but the empty directory lingered — surface it as a failure on the
                    // directory without invalidating the file moves that already succeeded.
                    if (dirCleanupError is not null)
                    {
                        context.MarkFailed(child);
                        yield return new TransformResult(
                            IsSuccess: false,
                            SourceNode: child,
                            SourceNodeResult: SourceResult.Moved,
                            ErrorMessage: $"Moved contents but source directory could not be deleted: {dirCleanupError}");
                    }
                }
            }
        }

        // --- Files in this directory: rename when atomic, else copy-then-delete. -----------------
        foreach (var file in node.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (!file.IsSelected || context.IsNodeFailed(file)) continue;

            var fileCtx = context.GetNodeContext(file);
            var fileDest = targetProvider.JoinPath(destinationPath, fileCtx.PathSegments);
            var fileDestExists = await targetProvider.ExistsAsync(fileDest, ct);

            // Pre-existing destination under Skip: leave the source in place.
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

            string? fileError = null;
            if (canAtomicMove)
            {
                // Same-volume, capable provider: a rename is the whole operation.
                try
                {
                    await context.SourceProvider.MoveAsync(file.FullPath, fileDest, ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    fileError = ex.Message;
                }
            }
            else
            {
                // Cross-volume / non-atomic: copy via the shared strategy, then delete the source.
                // `copied` distinguishes a transfer failure from a source-cleanup failure so the
                // message is accurate (a copied-but-not-deleted file is not data loss).
                bool copied = false;
                try
                {
                    await strategy.TransferFileAsync(context, file, targetProvider, fileDest, ct);
                    copied = true;
                    await context.SourceProvider.DeleteAsync(file.FullPath, ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    fileError = copied
                        ? $"Copied but source could not be deleted: {ex.Message}"
                        : ex.Message;
                }
            }

            if (fileError is not null)
            {
                context.MarkFailed(file);
                yield return new TransformResult(
                    IsSuccess: false,
                    SourceNode: file,
                    SourceNodeResult: SourceResult.Skipped,
                    ErrorMessage: fileError);
                continue;
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
    /// Returns true when an entire directory subtree is fully checked and all files are
    /// filter-included — the precondition for both the atomic unit move and the post-move deletion
    /// of the (now empty) source directory. A partial selection fails this and forces a piecewise walk.
    /// </summary>
    private static bool CanMoveEntireSubtree(DirectoryNode node)
    {
        if (node.CheckState != CheckState.Checked) return false;
        if (!node.Files.All(f => f.FilterResult == FilterResult.Included)) return false;
        foreach (var child in node.Children)
            if (!CanMoveEntireSubtree(child)) return false;
        return true;
    }
}
