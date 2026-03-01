using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class SelectAllStep : IPipelineStep
{
    public StepKind StepType => StepKind.SelectAll;
    public bool IsExecutable => false;
    public bool IsConfigurable => false;

    public void Validate(StepValidationContext context)
    {
        // No preconditions. Post-condition: reset SourceExists so downstream steps
        // are not blocked by a prior destructive step.
        context.SourceExists = true;
        context.HasSelectedIncludedInputs = true;
    }

    public TransformStepConfig Config => new(StepType, new JsonObject());

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        foreach (var node in ctx.RootNode.GetFilterIncludedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (!node.IsDirectory)
                ctx.GetNodeContext(node).VirtualCheckState = CheckState.Checked;
            yield return new TransformResult(
                IsSuccess: true,
                SourcePath: node.FullPath,
                SourcePathResult: SourcePathResult.None);
        }
    }

    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        foreach (var node in ctx.RootNode.GetFilterIncludedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (!node.IsDirectory)
                node.CheckState = CheckState.Checked;
            yield return new TransformResult(
                IsSuccess: true,
                SourcePath: node.FullPath,
                SourcePathResult: SourcePathResult.None);
        }
    }
}
