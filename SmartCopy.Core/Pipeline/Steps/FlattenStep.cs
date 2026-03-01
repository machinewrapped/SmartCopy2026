using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class FlattenStep : ITransformStep
{
    public FlattenStep(FlattenConflictStrategy conflictStrategy = FlattenConflictStrategy.AutoRenameCounter)
    {
        ConflictStrategy = conflictStrategy;
    }

    public FlattenConflictStrategy ConflictStrategy { get; set; }

    public StepKind StepType => StepKind.Flatten;
    public bool IsExecutable => false;

    public TransformStepConfig Config => new(StepType, new JsonObject { ["conflictStrategy"] = ConflictStrategy.ToString() });

    public void Validate(StepValidationContext context)
    {
        context.ValidateSourceExists("Flatten");
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        foreach (var node in ctx.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (ctx.IsNodeFailed(node)) continue;
            ApplyToContext(ctx.GetNodeContext(node));
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
        foreach (var node in ctx.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (ctx.IsNodeFailed(node)) continue;
            ApplyToContext(ctx.GetNodeContext(node));
            yield return new TransformResult(
                IsSuccess: true,
                SourcePath: node.FullPath,
                SourcePathResult: SourcePathResult.None);
        }
    }

    private static void ApplyToContext(TransformContext context)
    {
        if (context.PathSegments.Length > 0)
            context.PathSegments = [context.PathSegments[^1]];
    }
}
