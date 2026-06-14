using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

/// <summary>
/// Executable pipeline step that copies the selected files into a destination tree, preserving
/// each node's (possibly transformed) relative path.
/// <para>
/// The step itself owns only <em>orchestration</em>: it resolves the destination provider, opens a
/// bulk-write scope, and enumerates the selection. The actual byte transfer — buffer sizing,
/// batching, progress, IO-error handling — is delegated to an <see cref="Strategy.ICopyStrategy"/>
/// chosen per source→destination pair by the policy on <see cref="IStepContext"/>. This keeps the
/// transfer mechanics shared with <see cref="MoveStep"/>'s copy-then-delete fallback.
/// </para>
/// </summary>
public sealed class CopyStep : IPipelineStep, IHasDestinationPath, IHasFreeSpaceCheck
{
    public StepKind StepType => StepKind.Copy;
    public bool IsExecutable => true;

    public CopyStep(string destinationPath, OverwriteMode overwriteMode = OverwriteMode.Skip)
    {
        DestinationPath = destinationPath;
        OverwriteMode = overwriteMode;
    }

    /// <summary>How to treat a file that already exists at the destination (Skip / Overwrite).</summary>
    public OverwriteMode OverwriteMode { get; set; }

    /// <summary>
    /// When true and <see cref="OverwriteMode"/> is not Skip, the per-file destination-exists probe
    /// is omitted (the write overwrites unconditionally). Trades the Created/Overwritten distinction
    /// for one fewer stat per file — a negligible saving in practice.
    /// </summary>
    public bool SkipExistsCheckForOverwrite { get; set; }

    /// <summary>Rebuilds a step from its serialized config (workflow presets, .sc2 pipelines).</summary>
    internal static CopyStep FromConfig(TransformStepConfig config) =>
        new(config.GetRequired("destinationPath"),
            config.ParseEnum("overwriteMode", OverwriteMode.Skip));

    /// <summary>Serializes this step's destination and overwrite mode for persistence.</summary>
    public TransformStepConfig Config => new(StepType, new JsonObject
    {
        ["destinationPath"] = DestinationPath,
        ["overwriteMode"] = OverwriteMode.ToString()
    });

    public string AutoSummary => HasDestinationPath ? $"Copy to {PathHelper.GetFriendlyTarget(DestinationPath)}" : StepType.ForDisplay();
    public string Description => HasDestinationPath ? $"Copy to {DestinationPath} ({OverwriteMode})" : "Destination required";

    private string? _destinationPath;

    /// <summary>Root path of the destination tree; files land at <c>DestinationPath + relativeSegments</c>.</summary>
    public string? DestinationPath
    {
        get => _destinationPath;
        set => _destinationPath = value;
    }

    public bool HasDestinationPath => !string.IsNullOrWhiteSpace(DestinationPath);

    /// <summary>
    /// Pre-execution free-space check (drives the amber warning on the step card). Returns null when
    /// there is nothing to validate — no bytes selected, no destination, an unresolvable provider, or
    /// a provider that cannot report free space — so the caller treats it as "no warning".
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

        // Free space is cached per provider so repeated validations during editing stay cheap.
        var cachedFreeSpace = freeSpaceCache.GetForProvider(target);
        if (cachedFreeSpace is null) return null;

        return new FreeSpaceValidationResult(bytesNeeded, cachedFreeSpace.Value, target.RootPath);
    }

    /// <summary>
    /// Static validation run before execution: a copy needs at least one selected input and a
    /// destination path. Also surfaces the free-space warning. Does no I/O beyond the cached check.
    /// </summary>
    public Task Validate(StepValidationContext context, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        context.ValidateHasSelectedInputs();
        context.ValidateSourceExists("Copy");
        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            context.AddBlockingIssue("Step.MissingDestination", "Copy requires a destination path.");
        }
        context.AddFreeSpaceWarning(this);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Produces the planned actions for the preview pane without touching any bytes. Mirrors the
    /// per-file decisions <see cref="ApplyAsync"/> will make (skip vs create vs overwrite) so the user
    /// sees an accurate plan before confirming a destructive run.
    /// </summary>
    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        if (DestinationPath is null)
        {
            yield break;
        }

        foreach (var node in context.GetPreviewSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;

            // Directories are structure, not bytes — report a no-op so the tree renders but nothing copies.
            if (node is DirectoryNode)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.None);
                continue;
            }

            // Resolve the provider per node: PathSegments may have been rewritten by earlier steps
            // (flatten/rename), so the destination depends on the node's current context, not the source.
            var nodeCtx = context.GetNodeContext(node);
            var targetProvider = nodeCtx.ResolveProvider(DestinationPath)
                ?? throw new InvalidOperationException($"No IFileSystemProvider for path {DestinationPath}");

            var destination = targetProvider.JoinPath(DestinationPath, nodeCtx.PathSegments);

            // A pre-existing destination under Skip means the file is left untouched — preview it as skipped.
            var destinationExists = await targetProvider.ExistsAsync(destination, ct);
            if (destinationExists && OverwriteMode == OverwriteMode.Skip)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.Skipped,
                    DestinationPath: destination,
                    NumberOfFilesSkipped: 1,
                    InputBytes: node.Size);
                continue;
            }

            // Otherwise the file will be written; flag whether that overwrites existing data so the
            // preview can highlight destructive actions.
            var destResult = destinationExists
                ? DestinationResult.Overwritten
                : DestinationResult.Created;

            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.Copied,
                DestinationPath: destination,
                DestinationResult: destResult,
                NumberOfFilesAffected: 1,
                InputBytes: node.Size,
                OutputBytes: node.Size);
        }
    }

    /// <summary>
    /// Executes the copy. Resolves the destination provider and copy strategy once, then streams the
    /// whole selection through the strategy, relaying one <see cref="TransformResult"/> per file.
    /// </summary>
    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        if (DestinationPath is null)
        {
            throw new InvalidOperationException("DestinationPath must be set for CopyStep.");
        }

        // Every selected file shares one destination provider, resolved from the root.
        var targetProvider = context.GetNodeContext(context.RootNode).ResolveProvider(DestinationPath)
            ?? throw new InvalidOperationException("TargetProvider must be set for CopyStep.");

        // Bulk-write scope lets providers establish protocol-level transfer sessions (e.g. MTP).
        await using var _ = targetProvider.BeginBulkWriteAsync();

        // The policy resolves buffer size + batching from the source/destination drive pair and
        // returns the strategy that performs the byte transfer. Copy keeps no transfer logic of
        // its own — it just labels the outcome (Copied) and forwards the strategy's results.
        var strategy = await context.ResolveCopyStrategyAsync(targetProvider, ct);

        await foreach (var result in strategy.CopySelectionAsync(
            context, targetProvider, DestinationPath, OverwriteMode, SkipExistsCheckForOverwrite, SourceResult.Copied, ct))
        {
            yield return result;
        }
    }
}
