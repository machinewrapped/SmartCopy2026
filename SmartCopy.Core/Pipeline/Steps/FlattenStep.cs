using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class FlattenStep : IPipelineStep
{
    public FlattenStep(FlattenConflictStrategy conflictStrategy = FlattenConflictStrategy.AutoRenameCounter)
    {
        ConflictStrategy = conflictStrategy;
    }

    public FlattenConflictStrategy ConflictStrategy { get; set; }

    public StepKind StepType => StepKind.Flatten;
    public bool IsExecutable => false;

    public string AutoSummary => StepType.ForDisplay();
    public string Description => $"Conflict strategy: {ConflictStrategy}";

    public TransformStepConfig Config => new(StepType, new JsonObject { ["conflictStrategy"] = ConflictStrategy.ToString() });

    public void Validate(StepValidationContext context)
    {
        context.ValidateSourceExists("Flatten");
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        foreach (var node in context.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;
            ApplyToContext(context.GetNodeContext(node));
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
        foreach (var node in context.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;
            ApplyToContext(context.GetNodeContext(node));
            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.None);
        }
    }

    private static void ApplyToContext(PipelineContext context)
    {
        if (context.PathSegments.Length > 0)
            context.PathSegments = [context.PathSegments[^1]];
    }
}
