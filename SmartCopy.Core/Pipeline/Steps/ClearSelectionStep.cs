using System.Text.Json.Nodes;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class ClearSelectionStep : ITransformStep
{
    public StepKind StepType => StepKind.ClearSelection;
    public bool IsExecutable => false;
    public bool IsConfigurable => false;
    public bool ProvidesInput => true;

    public TransformStepConfig Config => new(StepType, new JsonObject());

    public void Validate(StepValidationContext context)
    {
        // No preconditions or postconditions.
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(TransformContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        context.SourceNode.CheckState = CheckState.Unchecked;
        yield return new TransformResult(Success: true, StepType: StepType, DestinationPath: null, Message: "Clear selection");
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        context.SourceNode.CheckState = CheckState.Unchecked;
        return Task.FromResult(new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.DisplayPath,
            Message: "Clear selection"));
    }
}
