using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class CopyStep : IPipelineStep, IHasDestinationPath
{
    public StepKind StepType => StepKind.Copy;
    public bool IsExecutable => true;

    public CopyStep(string destinationPath)
    {
        DestinationPath = destinationPath;
    }

    public TransformStepConfig Config => new(StepType, new JsonObject { ["destinationPath"] = DestinationPath });

    public string AutoSummary => HasDestinationPath ? $"Copy to {PathHelper.GetFriendlyTarget(DestinationPath)}" : StepType.ForDisplay();
    public string Description => HasDestinationPath ? $"Copy to {DestinationPath}" : "Destination required";

    private string? _destinationPath;
    public string? DestinationPath 
    { 
        get => _destinationPath; 
        set => _destinationPath = value; 
    }

    public bool HasDestinationPath => !string.IsNullOrWhiteSpace(DestinationPath);

    public void Validate(StepValidationContext context)
    {
        context.ValidateHasSelectedInputs();
        context.ValidateSourceExists("Copy");
        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            context.AddBlockingIssue("Step.MissingDestination", "Copy requires a destination path.");
        }
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

            var destResult = await targetProvider.ExistsAsync(destination, ct)
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

            if (destinationExists && context.OverwriteMode == OverwriteMode.Skip)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.None,
                    DestinationPath: destination,
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
