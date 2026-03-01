using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class ClearSelectionStep : ITransformStep
{
    public StepKind StepType => StepKind.ClearSelection;
    public bool IsExecutable => false;
    public bool IsConfigurable => false;

    public TransformStepConfig Config => new(StepType, new JsonObject());

    public void Validate(StepValidationContext context)
    {
        // No preconditions or postconditions.
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        foreach (var node in ctx.RootNode.GetFilterIncludedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (!node.IsDirectory)
                node.CheckState = CheckState.Unchecked;
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
                node.CheckState = CheckState.Unchecked;
            yield return new TransformResult(
                IsSuccess: true,
                SourcePath: node.FullPath,
                SourcePathResult: SourcePathResult.None);
        }
    }
}
