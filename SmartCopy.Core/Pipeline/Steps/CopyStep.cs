using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class CopyStep : ITransformStep
{
    public CopyStep(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Destination path must not be empty.", nameof(destinationPath));
        }

        DestinationPath = destinationPath;
    }

    public string DestinationPath { get; }

    public string StepType => "Copy";
    public bool IsPathStep => false;
    public bool IsContentStep => false;
    public bool IsExecutable => true;

    public TransformStepConfig Config => new(
        StepType,
        new JsonObject { ["destinationPath"] = DestinationPath });

    public TransformResult Preview(TransformContext context)
    {
        var destination = StepPathHelper.BuildDestinationPath(DestinationPath, context.CurrentPath);
        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: destination,
            OutputBytes: context.SourceNode.Size,
            Message: "Copy preview");
    }

    public async Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var targetProvider = context.TargetProvider
                             ?? throw new InvalidOperationException("TargetProvider must be set for CopyStep.");

        var destination = StepPathHelper.BuildDestinationPath(DestinationPath, context.CurrentPath);
        var destinationExists = await targetProvider.ExistsAsync(destination, ct);
        if (destinationExists && context.OverwriteMode == OverwriteMode.Skip)
        {
            return new TransformResult(
                Success: true,
                StepType: StepType,
                DestinationPath: destination,
                OutputBytes: 0,
                Message: "Skipped existing destination.");
        }

        await using var sourceStream = context.ContentStream
                                       ?? await context.SourceProvider.OpenReadAsync(context.SourceNode.FullPath, ct);
        await targetProvider.WriteAsync(destination, sourceStream, progress: null, ct);

        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: destination,
            OutputBytes: context.SourceNode.Size,
            Message: "Copied");
    }
}

