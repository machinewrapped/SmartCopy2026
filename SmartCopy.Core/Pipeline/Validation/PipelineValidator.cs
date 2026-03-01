using System.Collections.Generic;

namespace SmartCopy.Core.Pipeline.Validation;

public sealed class PipelineValidator
{
    public static PipelineValidationResult Validate(
        IReadOnlyList<IPipelineStep> steps,
        PipelineValidationContext? context = null)
    {
        context ??= new PipelineValidationContext();
        var ctx = new StepValidationContext(context.HasSelectedIncludedInputs);

        if (steps.Count == 0)
        {
            ctx.AddPipelineIssue("Pipeline.Empty", "Pipeline is empty.", PipelineValidationSeverity.Blocking);
            return new PipelineValidationResult(ctx.Issues);
        }

        if (steps.Any(step => step.IsExecutable) == false)
        {
            ctx.AddPipelineIssue("Pipeline.NoExecutableStep", "Pipeline has no executable steps.", PipelineValidationSeverity.Blocking);
            return new PipelineValidationResult(ctx.Issues);
        }

        for (var i = 0; i < steps.Count; i++)
        {
            ctx.StepIndex = i;
            steps[i].Validate(ctx);

            if (ctx.HasBlockingIssue)
            {
                break;
            }
        }

        return new PipelineValidationResult(ctx.Issues);
    }
}
