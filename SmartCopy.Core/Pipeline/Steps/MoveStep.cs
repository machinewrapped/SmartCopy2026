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
        var destResult = await targetProvider.ExistsAsync(destination, ct)
            ? DestinationPathResult.Overwritten
            : DestinationPathResult.Created;

        yield return new TransformResult(
            IsSuccess: true,
            SourcePath: context.SourceNode.FullPath,
            SourcePathResult: SourcePathResult.Moved,
            DestinationPath: destination,
            DestinationPathResult: destResult,
            NumberOfFilesAffected: context.SourceNode.CountSelectedFiles(),
            NumberOfFoldersAffected: context.SourceNode.CountSelectedFolders(),
            InputBytes: context.SourceNode.Size,
            OutputBytes: context.SourceNode.Size);
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
            {
                return new TransformResult(
                    IsSuccess: false,
                    SourcePath: context.SourceNode.FullPath,
                    SourcePathResult: SourcePathResult.None);
            }

            await context.SourceProvider.MoveAsync(context.SourceNode.FullPath, destination, ct);
            return new TransformResult(
                IsSuccess: true,
                SourcePath: context.SourceNode.FullPath,
                SourcePathResult: SourcePathResult.Moved,
                DestinationPath: destination,
                DestinationPathResult: DestinationPathResult.Created,
                NumberOfFoldersAffected: 1,
                InputBytes: context.SourceNode.Size,
                OutputBytes: context.SourceNode.Size);
        }

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

        if (ReferenceEquals(targetProvider, context.SourceProvider) && targetProvider.Capabilities.CanAtomicMove)
        {
            await context.SourceProvider.MoveAsync(context.SourceNode.FullPath, destination, ct);
            return new TransformResult(
                IsSuccess: true,
                SourcePath: context.SourceNode.FullPath,
                SourcePathResult: SourcePathResult.Moved,
                DestinationPath: destination,
                DestinationPathResult: destinationExists ? DestinationPathResult.Overwritten : DestinationPathResult.Created,
                NumberOfFilesAffected: 1,
                InputBytes: context.SourceNode.Size,
                OutputBytes: context.SourceNode.Size);
        }

        await using var sourceStream = context.ContentStream
                                       ?? await context.SourceProvider.OpenReadAsync(context.SourceNode.FullPath, ct);

        await targetProvider.WriteAsync(destination, sourceStream, progress: null, ct);
        await context.SourceProvider.DeleteAsync(context.SourceNode.FullPath, ct);

        return new TransformResult(
            IsSuccess: true,
            SourcePath: context.SourceNode.FullPath,
            SourcePathResult: SourcePathResult.Moved,
            DestinationPath: destination,
            DestinationPathResult: destinationExists ? DestinationPathResult.Overwritten : DestinationPathResult.Created,
            NumberOfFilesAffected: 1,
            InputBytes: context.SourceNode.Size,
            OutputBytes: context.SourceNode.Size);
    }
}
