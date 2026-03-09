using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class CopyStep : IPipelineStep, IHasDestinationPath, IHasFreeSpaceCheck
{
    public StepKind StepType => StepKind.Copy;
    public bool IsExecutable => true;

    public CopyStep(string destinationPath, OverwriteMode overwriteMode = OverwriteMode.Skip)
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

    public string AutoSummary => HasDestinationPath ? $"Copy to {PathHelper.GetFriendlyTarget(DestinationPath)}" : StepType.ForDisplay();
    public string Description => HasDestinationPath ? $"Copy to {DestinationPath} ({OverwriteMode})" : "Destination required";

    private string? _destinationPath;
    public string? DestinationPath 
    { 
        get => _destinationPath; 
        set => _destinationPath = value; 
    }

    public bool HasDestinationPath => !string.IsNullOrWhiteSpace(DestinationPath);

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

        var cachedFreeSpace = freeSpaceCache.GetForProvider(target);
        if (cachedFreeSpace is null) return null;

        return new FreeSpaceValidationResult(bytesNeeded, cachedFreeSpace.Value, target.RootPath);
    }

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

            if (node.IsDirectory)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.None);
                continue;
            }

            var nodeCtx = context.GetNodeContext(node);
            var targetProvider = nodeCtx.ResolveProvider(DestinationPath)
                ?? throw new InvalidOperationException($"No IFileSystemProvider for path {DestinationPath}");

            var destination = targetProvider.JoinPath(DestinationPath, nodeCtx.PathSegments);

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

    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        if (DestinationPath is null)
        {
            throw new InvalidOperationException("DestinationPath must be set for CopyStep.");
        }

        foreach (var node in context.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;

            if (node.IsDirectory)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.None);
                continue;
            }

            var nodeCtx = context.GetNodeContext(node);
            var targetProvider = nodeCtx.ResolveProvider(DestinationPath)
                ?? throw new InvalidOperationException("TargetProvider must be set for CopyStep.");

            var destination = targetProvider.JoinPath(DestinationPath, nodeCtx.PathSegments);
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

            await using var sourceStream = await context.SourceProvider.OpenReadAsync(node.FullPath, ct);
            await targetProvider.WriteAsync(destination, sourceStream, progress: null, ct);

            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.Copied,
                DestinationPath: destination,
                DestinationResult: destinationExists ? DestinationResult.Overwritten : DestinationResult.Created,
                NumberOfFilesAffected: 1,
                InputBytes: node.Size,
                OutputBytes: node.Size);
        }
    }
}
