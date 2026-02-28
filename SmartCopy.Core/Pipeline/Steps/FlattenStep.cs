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

    public TransformStepConfig Config => new(StepType, new JsonObject { ["conflictStrategy"] = ConflictStrategy.ToString() });

    public void Validate(StepValidationContext context)
    {
        context.ValidateSourceExists("Flatten");
        // Post-condition: source is unchanged.
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(TransformContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        Apply(context);
        yield return new TransformResult(
            IsSuccess: true,
            SourcePath: context.SourceNode.FullPath,
            SourcePathResult: SourcePathResult.None);
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Apply(context);
        return Task.FromResult(new TransformResult(
            IsSuccess: true,
            SourcePath: context.SourceNode.FullPath,
            SourcePathResult: SourcePathResult.None));
    }

    private static void Apply(TransformContext context)
    {
        if (context.PathSegments.Length > 0)
        {
            context.PathSegments = [context.PathSegments[^1]];
        }
    }
}
