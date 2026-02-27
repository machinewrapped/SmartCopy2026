using System.Collections.Generic;
using System.Linq;

namespace SmartCopy.Core.Pipeline.Validation;

public sealed class PipelineValidator
{
    public static PipelineValidationResult Validate(
        IReadOnlyList<ITransformStep> steps,
        PipelineValidationContext? context = null)
    {
        context ??= new PipelineValidationContext();
        var issues = new List<PipelineValidationIssue>();

        if (steps.Count == 0)
        {
            issues.Add(new PipelineValidationIssue(
                StepIndex: null,
                Code: "Pipeline.Empty",
                Message: "Pipeline must contain at least one step.",
                Severity: PipelineValidationSeverity.Blocking));
            return new PipelineValidationResult(issues);
        }

        var hasExecutable = false;
        var hasExecutableRequiringSelectedInputs = false;
        var sourceExists = true;

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            // Delegate step-scoped configuration validation to the step itself.
            issues.AddRange(step.Validate(i));

            hasExecutable |= step.IsExecutable;
            hasExecutableRequiringSelectedInputs |= step.RequiresSelectedIncludedInputs;

            if (step.RequiresSourceExists && !sourceExists)
            {
                issues.Add(new PipelineValidationIssue(
                    StepIndex: i,
                    Code: "Step.SourceMissing",
                    Message: $"{step.StepType} cannot run because the source no longer exists after earlier steps.",
                    Severity: PipelineValidationSeverity.Blocking));
            }

            if (step.SetsSourceExists.HasValue)
            {
                sourceExists = step.SetsSourceExists.Value;
            }
        }

        if (hasExecutableRequiringSelectedInputs && !context.HasSelectedIncludedInputs)
        {
            issues.Add(new PipelineValidationIssue(
                StepIndex: null,
                Code: "Pipeline.NoSelectedInputs",
                Message: "At least one file must be selected.",
                Severity: PipelineValidationSeverity.Blocking));
        }

        if (!hasExecutable)
        {
            issues.Add(new PipelineValidationIssue(
                StepIndex: null,
                Code: "Pipeline.NoExecutableStep",
                Message: "Pipeline must contain at least one executable step.",
                Severity: PipelineValidationSeverity.Blocking));
        }

        return new PipelineValidationResult(issues);
    }
}
