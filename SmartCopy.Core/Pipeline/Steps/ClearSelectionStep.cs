using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class ClearSelectionStep : IPipelineStep
{
    public StepKind StepType => StepKind.ClearSelection;
    public bool IsExecutable => false;
    public bool IsConfigurable => false;

    public string AutoSummary => StepType.ForDisplay();
    public string Description => "Deselect all files";

    public TransformStepConfig Config => new(StepType, new JsonObject());

    public Task Validate(StepValidationContext context, CancellationToken ct = default)
    {
        context.SelectedBytes = 0;
        context.SelectedFileCount = 0;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        foreach (var node in context.RootNode.GetFilterIncludedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (node is FileNode fileNode)
                context.GetNodeContext(fileNode).VirtualCheckState = CheckState.Unchecked;
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
            if (node is FileNode)
                node.CheckState = CheckState.Unchecked;
            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.None);
        }

        context.RootNode.BuildStats();
    }
}
