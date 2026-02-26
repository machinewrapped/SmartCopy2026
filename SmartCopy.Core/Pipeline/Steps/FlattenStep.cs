using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class FlattenStep : ITransformStep
{
    public FlattenStep(FlattenConflictStrategy conflictStrategy = FlattenConflictStrategy.AutoRenameCounter)
    {
        ConflictStrategy = conflictStrategy;
    }

    public FlattenConflictStrategy ConflictStrategy { get; set; }

    public StepKind StepType => StepKind.Flatten;
    public bool IsPathStep => true;
    public bool IsContentStep => false;
    public bool IsExecutable => false;

    public TransformStepConfig Config => new(
        StepType,
        new JsonObject
        {
            ["conflictStrategy"] = ConflictStrategy.ToString(),
        });

    public TransformResult Preview(TransformContext context)
    {
        Apply(context);
        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.DisplayPath,
            Message: "Path flattened");
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Apply(context);
        return Task.FromResult(new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.DisplayPath,
            Message: "Path flattened"));
    }

    private static void Apply(TransformContext context)
    {
        if (context.PathSegments.Length > 0)
        {
            context.PathSegments = [context.PathSegments[^1]];
        }
    }
}
