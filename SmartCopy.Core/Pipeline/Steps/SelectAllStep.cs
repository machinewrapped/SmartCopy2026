using System.Text.Json.Nodes;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class SelectAllStep : ITransformStep
{
    public StepKind StepType => StepKind.SelectAll;
    public bool IsExecutable => false;
    public bool IsConfigurable => false;
    public bool ProvidesInput => true;

    public void Validate(StepValidationContext context)
    {
        // No preconditions. Post-condition: reset SourceExists so downstream steps
        // are not blocked by a prior destructive step.
        context.SourceExists = true;
        context.HasSelectedIncludedInputs = true;
    }

    public TransformStepConfig Config => new(StepType, new JsonObject());

    public TransformResult Preview(TransformContext context)
    {
        context.SourceNode.CheckState = CheckState.Checked;
        return new(Success: true, StepType: StepType, DestinationPath: null, Message: "Mark as selected");
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        context.SourceNode.CheckState = CheckState.Checked;
        return Task.FromResult(new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.DisplayPath,
            Message: "Mark as selected"));
    }
}
