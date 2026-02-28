using System.Text.Json.Nodes;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class InvertSelectionStep : ITransformStep
{
    public StepKind StepType => StepKind.InvertSelection;
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

    public async IAsyncEnumerable<TransformResult> PreviewAsync(TransformContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        context.SourceNode.CheckState = context.SourceNode.CheckState == CheckState.Checked
            ? CheckState.Unchecked
            : CheckState.Checked;
        yield return new TransformResult(Success: true, StepType: StepType, DestinationPath: null, Message: "Invert selection");
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        context.SourceNode.CheckState = context.SourceNode.CheckState == CheckState.Checked
            ? CheckState.Unchecked
            : CheckState.Checked;
        return Task.FromResult(new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.DisplayPath,
            Message: "Invert selection"));
    }
}
