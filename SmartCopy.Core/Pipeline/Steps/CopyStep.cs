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

    public TransformStepConfig Config => new(StepType, new JsonObject { ["destinationPath"] = DestinationPath });

    public void Validate(StepValidationContext context)
    {
        context.ValidateHasSelectedInputs();
        context.ValidateSourceExists("Copy");
        if (string.IsNullOrWhiteSpace(DestinationPath))
            context.AddBlockingIssue("Step.MissingDestination", "Copy requires a destination path.");
        // Post-condition: source is still present after a copy.
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(TransformContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (context.SourceNode.IsDirectory)
        {
            yield return new TransformResult(
                IsSuccess: true,
                SourcePath: context.SourceNode.FullPath,
                SourcePathResult: SourcePathResult.None);
        }

        var targetProvider = context.TargetProvider
                             ?? throw new InvalidOperationException("TargetProvider must be set for CopyStep.");

        var destination = StepPathHelper.BuildDestinationPath(targetProvider, DestinationPath, context.PathSegments);
        var destResult = await targetProvider.ExistsAsync(destination, ct)
            ? DestinationPathResult.Overwritten
            : DestinationPathResult.Created;

        yield return new TransformResult(
            IsSuccess: true,
            SourcePath: context.SourceNode.FullPath,
            SourcePathResult: SourcePathResult.Copied,
            DestinationPath: destination,
            DestinationPathResult: destResult,
            NumberOfFilesAffected: 1,
            InputBytes: context.SourceNode.Size,
            OutputBytes: context.SourceNode.Size);
    }

    public async Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (context.SourceNode.IsDirectory)
        {
            return new TransformResult(
                IsSuccess: true,
                SourcePath: context.SourceNode.FullPath,
                SourcePathResult: SourcePathResult.None);
        }

        var targetProvider = context.TargetProvider
                             ?? throw new InvalidOperationException("TargetProvider must be set for CopyStep.");

        var destination = StepPathHelper.BuildDestinationPath(targetProvider, DestinationPath, context.PathSegments);
        var destinationExists = await targetProvider.ExistsAsync(destination, ct);
        if (destinationExists && context.OverwriteMode == OverwriteMode.Skip)
        {
            return new TransformResult(
                IsSuccess: true,
                SourcePath: context.SourceNode.FullPath,
                SourcePathResult: SourcePathResult.None,
                DestinationPath: destination,
                InputBytes: context.SourceNode.Size);
        }

        await using var sourceStream = context.ContentStream
                                       ?? await context.SourceProvider.OpenReadAsync(context.SourceNode.FullPath, ct);
        await targetProvider.WriteAsync(destination, sourceStream, progress: null, ct);

        return new TransformResult(
            IsSuccess: true,
            SourcePath: context.SourceNode.FullPath,
            SourcePathResult: SourcePathResult.Copied,
            DestinationPath: destination,
            DestinationPathResult: destinationExists ? DestinationPathResult.Overwritten : DestinationPathResult.Created,
            NumberOfFilesAffected: 1,
            InputBytes: context.SourceNode.Size,
            OutputBytes: context.SourceNode.Size);
    }
}
