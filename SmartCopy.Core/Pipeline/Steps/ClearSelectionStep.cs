using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class ClearSelectionStep : IPipelineStep
{
    public StepKind StepType => StepKind.ClearSelection;
    public PipelineStepDisplayInfo Display => new(StepType.ForDisplay(), "Unmark all files");
    public bool IsExecutable => false;
    public bool IsConfigurable => false;

    public TransformStepConfig Config => new(StepType, new JsonObject());

    public void Validate(StepValidationContext context)
    {
        // No preconditions or postconditions.
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        foreach (var node in context.RootNode.GetFilterIncludedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (!node.IsDirectory)
                context.GetNodeContext(node).VirtualCheckState = CheckState.Unchecked;
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
                node.CheckState = CheckState.Unchecked;
            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.None);
        }
    }
}
