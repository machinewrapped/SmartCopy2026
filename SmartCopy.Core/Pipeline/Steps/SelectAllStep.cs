using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class SelectAllStep : IPipelineStep
{
    public StepKind StepType => StepKind.SelectAll;
    public bool IsExecutable => false;
    public bool IsConfigurable => false;

    public string AutoSummary => StepType.ForDisplay();
    public string Description => "Mark all files as selected";

    public void Validate(StepValidationContext context)
    {
        // No preconditions. Post-condition: reset SourceExists so downstream steps
        // are not blocked by a prior destructive step.
        context.SourceExists = true;
        context.HasSelectedIncludedInputs = true;
    }

    public TransformStepConfig Config => new(StepType, new JsonObject());

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        foreach (var node in context.RootNode.GetFilterIncludedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (!node.IsDirectory)
                context.GetNodeContext(node).VirtualCheckState = CheckState.Checked;
            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.None);
        }
    }

    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        foreach (var node in context.RootNode.GetFilterIncludedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (!node.IsDirectory)
                node.CheckState = CheckState.Checked;
            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.None);
        }
    }
}
