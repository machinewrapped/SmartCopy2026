using System.Text.Json.Nodes;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class ClearSelectionStep : ITransformStep
{
    public StepKind StepType => StepKind.ClearSelection;
    public bool IsExecutable => false;
    public bool IsConfigurable => false;

    public void Validate(StepValidationContext context)
    {
        // No preconditions or postconditions.
    }

    public TransformStepConfig Config => new(StepType, new JsonObject());

    public TransformResult Preview(TransformContext context) =>
        new(Success: true, StepType: StepType, DestinationPath: context.DisplayPath, Message: "Clear selection");

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
