using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class MoveStep : ITransformStep
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
        // Post-condition: move consumes the source.
        context.SourceExists = false;
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(TransformContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var targetProvider = context.TargetProvider 
                             ?? throw new InvalidOperationException("TargetProvider must be set for MoveStep.");

        var destination = StepPathHelper.BuildDestinationPath(targetProvider, DestinationPath, context.PathSegments);
        
        PlanWarning? warning = null;
        if (await targetProvider.ExistsAsync(destination, ct))
        {
            warning = PlanWarning.DestinationOverwritten;
        }

        yield return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: destination,
            InputBytes: context.SourceNode.Size,
            OutputBytes: context.SourceNode.Size,
            Message: "Move preview",
            SourcePath: context.SourceNode.FullPath,
            Warning: warning);
    }

    public async Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var targetProvider = context.TargetProvider
                             ?? throw new InvalidOperationException("TargetProvider must be set for MoveStep.");
        var destination = StepPathHelper.BuildDestinationPath(targetProvider, DestinationPath, context.PathSegments);

        if (context.SourceNode.IsDirectory)
        {
            if (!ReferenceEquals(targetProvider, context.SourceProvider) || !targetProvider.Capabilities.CanAtomicMove)
                return new TransformResult(Success: false, StepType: StepType,
                    Message: "Cannot atomically move directory across providers.",
                    SourcePath: context.SourceNode.FullPath);

            await context.SourceProvider.MoveAsync(context.SourceNode.FullPath, destination, ct);
            return new TransformResult(
                Success: true,
                StepType: StepType,
                DestinationPath: destination,
                InputBytes: context.SourceNode.Size,
                OutputBytes: context.SourceNode.Size,
                Message: "Directory moved atomically.",
                SourcePath: context.SourceNode.FullPath);
        }

        var destinationExists = await targetProvider.ExistsAsync(destination, ct);
        if (destinationExists && context.OverwriteMode == OverwriteMode.Skip)
        {
            return new TransformResult(
                Success: true,
                StepType: StepType,
                DestinationPath: destination,
                InputBytes: context.SourceNode.Size,
                OutputBytes: 0,
                Message: "Skipped existing destination.",
                SourcePath: context.SourceNode.FullPath);
        }

        if (ReferenceEquals(targetProvider, context.SourceProvider) && targetProvider.Capabilities.CanAtomicMove)
        {
            await context.SourceProvider.MoveAsync(context.SourceNode.FullPath, destination, ct);
            return new TransformResult(
                Success: true,
                StepType: StepType,
                DestinationPath: destination,
                InputBytes: context.SourceNode.Size,
                OutputBytes: context.SourceNode.Size,
                Message: "Moved atomically.",
                SourcePath: context.SourceNode.FullPath);
        }

        await using var sourceStream = context.ContentStream
                                       ?? await context.SourceProvider.OpenReadAsync(context.SourceNode.FullPath, ct);

        await targetProvider.WriteAsync(destination, sourceStream, progress: null, ct);
        await context.SourceProvider.DeleteAsync(context.SourceNode.FullPath, ct);

        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: destination,
            OutputBytes: context.SourceNode.Size,
            Message: "Moved via copy+delete.",
            SourcePath: context.SourceNode.FullPath);
    }
}
