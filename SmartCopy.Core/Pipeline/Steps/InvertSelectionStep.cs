using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class InvertSelectionStep : IPipelineStep
{
    public StepKind StepType => StepKind.InvertSelection;
    public bool IsExecutable => false;
    public bool IsConfigurable => false;

    public string AutoSummary => StepType.ForDisplay();
    public string Description => "Toggle selection status of each file";

    public TransformStepConfig Config => new(StepType, new JsonObject());

    public async Task Validate(StepValidationContext context)
    {
        // No preconditions. Post-condition: reset SourceExists so downstream steps
        // are not blocked by a prior destructive step.
        context.SourceExists = true;
        context.HasSelectedIncludedInputs = true;
        context.SelectedBytes = 0;
        context.ByteEstimateUnknown = true;
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        foreach (var node in context.RootNode.GetFilterIncludedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (!node.IsDirectory)
            {
                var nodeCtx = context.GetNodeContext(node);
                nodeCtx.VirtualCheckState = nodeCtx.VirtualCheckState == CheckState.Checked
                    ? CheckState.Unchecked
                    : CheckState.Checked;
            }
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
                node.CheckState = node.CheckState == CheckState.Checked
                    ? CheckState.Unchecked
                    : CheckState.Checked;
            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.None);
        }
    }
}
