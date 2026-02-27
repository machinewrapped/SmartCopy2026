using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class CopyStep : ITransformStep
{
    public CopyStep(string destinationPath)
    {
        DestinationPath = destinationPath;
    }

    public string DestinationPath { get; set; }

    public StepKind StepType => StepKind.Copy;
    public bool IsExecutable => true;

    public void Validate(StepValidationContext context)
    {
        context.ValidateHasSelectedInputs();
        if (!context.SourceExists)
            context.AddBlockingIssue("Step.SourceMissing",
                "Copy cannot run because the source no longer exists after earlier steps.");
        if (string.IsNullOrWhiteSpace(DestinationPath))
            context.AddBlockingIssue("Step.MissingDestination", "Copy requires a destination path.");
        // Post-condition: source is still present after a copy.
    }

    public TransformStepConfig Config => new(
        StepType,
        new JsonObject { ["destinationPath"] = DestinationPath });

    public TransformResult Preview(TransformContext context)
    {
        var destination = StepPathHelper.BuildDestinationPath(DestinationPath, context.PathSegments);
        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: destination,
            OutputBytes: context.SourceNode.Size,
            Message: "Copy preview",
            SourcePath: context.SourceNode.FullPath);
    }

    public async Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var targetProvider = context.TargetProvider
                             ?? throw new InvalidOperationException("TargetProvider must be set for CopyStep.");

        var destination = StepPathHelper.BuildDestinationPath(targetProvider, DestinationPath, context.PathSegments);
        var destinationExists = await targetProvider.ExistsAsync(destination, ct);
        if (destinationExists && context.OverwriteMode == OverwriteMode.Skip)
        {
            return new TransformResult(
                Success: true,
                StepType: StepType,
                DestinationPath: destination,
                OutputBytes: 0,
                Message: "Skipped existing destination.",
                SourcePath: context.SourceNode.FullPath);
        }

        await using var sourceStream = context.ContentStream
                                       ?? await context.SourceProvider.OpenReadAsync(context.SourceNode.FullPath, ct);
        await targetProvider.WriteAsync(destination, sourceStream, progress: null, ct);

        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: destination,
            OutputBytes: context.SourceNode.Size,
            Message: "Copied",
            SourcePath: context.SourceNode.FullPath);
    }
}
