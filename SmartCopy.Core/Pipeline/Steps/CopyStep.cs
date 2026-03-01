using System;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class CopyStep : IPipelineStep
{
    public CopyStep(string destinationPath)
    {
        DestinationPath = destinationPath;
    }

    public string DestinationPath { get; set; }

    public StepKind StepType => StepKind.Copy;
    public bool IsExecutable => true;

    public TransformStepConfig Config => new(StepType, new JsonObject { ["destinationPath"] = DestinationPath });

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
        var targetProvider = context.TargetProvider
            ?? throw new InvalidOperationException("TargetProvider must be set for CopyStep.");

        foreach (var node in context.GetPreviewSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;

            if (node.IsDirectory)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: SourcePathResult.None);
                continue;
            }

            var nodeCtx = context.GetNodeContext(node);
            var destination = StepPathHelper.BuildDestinationPath(DestinationPath, nodeCtx.PathSegments);
            var destResult = await targetProvider.ExistsAsync(
                StepPathHelper.BuildDestinationPath(targetProvider, DestinationPath, nodeCtx.PathSegments), ct)
                ? DestinationPathResult.Overwritten
                : DestinationPathResult.Created;

            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourcePathResult.Copied,
                DestinationPath: destination,
                DestinationPathResult: destResult,
                NumberOfFilesAffected: 1,
                InputBytes: node.Size,
                OutputBytes: node.Size);
        }
    }

    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        var targetProvider = context.TargetProvider
            ?? throw new InvalidOperationException("TargetProvider must be set for CopyStep.");

        foreach (var node in context.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;

            if (node.IsDirectory)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: SourcePathResult.None);
                continue;
            }

            var nodeCtx = context.GetNodeContext(node);
            var destination = StepPathHelper.BuildDestinationPath(targetProvider, DestinationPath, nodeCtx.PathSegments);
            var destinationExists = await targetProvider.ExistsAsync(destination, ct);

            if (destinationExists && context.OverwriteMode == OverwriteMode.Skip)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: SourcePathResult.None,
                    DestinationPath: destination,
                    InputBytes: node.Size);
                continue;
            }

            await using var sourceStream = await context.SourceProvider.OpenReadAsync(node.FullPath, ct);
            await targetProvider.WriteAsync(destination, sourceStream, progress: null, ct);

            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourcePathResult.Copied,
                DestinationPath: destination,
                DestinationPathResult: destinationExists ? DestinationPathResult.Overwritten : DestinationPathResult.Created,
                NumberOfFilesAffected: 1,
                InputBytes: node.Size,
                OutputBytes: node.Size);
        }
    }
}
