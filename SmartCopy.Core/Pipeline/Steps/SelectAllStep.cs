using System.Text.Json.Nodes;
using SmartCopy.Core.DirectoryTree;
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

    public async IAsyncEnumerable<TransformResult> PreviewAsync(TransformContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        context.SourceNode.CheckState = CheckState.Checked;
        yield return new TransformResult(
            IsSuccess: true,
            SourcePath: context.SourceNode.FullPath,
            SourcePathResult: SourcePathResult.None);
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        context.SourceNode.CheckState = CheckState.Checked;
        return Task.FromResult(new TransformResult(
            IsSuccess: true,
            SourcePath: context.SourceNode.FullPath,
            SourcePathResult: SourcePathResult.None));
    }
}
