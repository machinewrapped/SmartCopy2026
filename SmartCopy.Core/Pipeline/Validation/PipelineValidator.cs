using System.Collections.Generic;

namespace SmartCopy.Core.Pipeline.Validation;

public sealed class PipelineValidator
{
    public static PipelineValidationResult Validate(
        IReadOnlyList<ITransformStep> steps,
        PipelineValidationContext? context = null)
    {
        context ??= new PipelineValidationContext();
        var ctx = new StepValidationContext(context.HasSelectedIncludedInputs);

        if (steps.Count == 0)
        {
            ctx.AddPipelineIssue("Pipeline.Empty", "Pipeline must contain at least one step.");
            return new PipelineValidationResult(ctx.Issues);
        }

        var hasExecutable = false;

        for (var i = 0; i < steps.Count; i++)
        {
            ctx.StepIndex = i;
            steps[i].Validate(ctx);
            hasExecutable |= steps[i].IsExecutable;
            if (ctx.HasBlockingIssue) break;
        }

        if (!ctx.HasBlockingIssue && !hasExecutable)
            ctx.AddPipelineIssue("Pipeline.NoExecutableStep", "Pipeline must contain at least one executable step.");

        return new PipelineValidationResult(ctx.Issues);
    }
}
