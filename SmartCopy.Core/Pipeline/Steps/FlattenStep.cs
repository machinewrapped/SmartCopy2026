using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
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

    public void Validate(StepValidationContext context)
    {
        context.ValidateSourceExists("Flatten");
        // Post-condition: source is unchanged.
    }

    public TransformStepConfig Config => new(
        StepType,
        new JsonObject
        {
            ["conflictStrategy"] = ConflictStrategy.ToString(),
        });

    public async IAsyncEnumerable<TransformResult> PreviewAsync(TransformContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        Apply(context);
        if (context.SourceNode.IsDirectory)
        {
            yield return new TransformResult(Success: true, StepType: StepType, DestinationPath: null);
            yield break;
        }

        yield return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.DisplayPath,
            Message: "Path flattened");
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Apply(context);
        if (context.SourceNode.IsDirectory)
            return Task.FromResult(new TransformResult(Success: true, StepType: StepType, DestinationPath: null));
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
