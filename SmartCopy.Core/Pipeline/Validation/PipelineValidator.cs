using System;
using System.Collections.Generic;
using System.Linq;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.Core.Pipeline.Validation;

public sealed class PipelineValidator
{
    public PipelineValidationResult Validate(IReadOnlyList<ITransformStep> steps)
    {
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
        var sourceExists = true;

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            if (!PipelineStepContracts.TryGet(step.StepType, out var contract))
            {
                issues.Add(new PipelineValidationIssue(
                    StepIndex: i,
                    Code: "Step.UnknownType",
                    Message: $"Unknown step type '{step.StepType}'.",
                    Severity: PipelineValidationSeverity.Blocking));
                continue;
            }

            hasExecutable |= contract.IsExecutable;

            if (contract.RequiresDestinationPath && string.IsNullOrWhiteSpace(GetDestinationPath(step)))
            {
                issues.Add(new PipelineValidationIssue(
                    StepIndex: i,
                    Code: "Step.MissingDestination",
                    Message: $"{step.StepType} requires a destination path.",
                    Severity: PipelineValidationSeverity.Blocking));
            }

            if (step is RenameStep renameStep && string.IsNullOrWhiteSpace(renameStep.Pattern))
            {
                issues.Add(new PipelineValidationIssue(
                    StepIndex: i,
                    Code: "Step.RenamePatternRequired",
                    Message: "Rename requires a non-empty pattern.",
                    Severity: PipelineValidationSeverity.Blocking));
            }

            if (step is RebaseStep rebaseStep
                && string.IsNullOrWhiteSpace(rebaseStep.StripPrefix)
                && string.IsNullOrWhiteSpace(rebaseStep.AddPrefix))
            {
                issues.Add(new PipelineValidationIssue(
                    StepIndex: i,
                    Code: "Step.RebaseConfigRequired",
                    Message: "Rebase requires StripPrefix or AddPrefix.",
                    Severity: PipelineValidationSeverity.Blocking));
            }

            if (contract.RequiresSourceExists && !sourceExists)
            {
                issues.Add(new PipelineValidationIssue(
                    StepIndex: i,
                    Code: "Step.SourceMissing",
                    Message: $"{step.StepType} cannot run because the source no longer exists after earlier steps.",
                    Severity: PipelineValidationSeverity.Blocking));
            }

            if (string.Equals(step.StepType, "Delete", StringComparison.OrdinalIgnoreCase)
                && i != steps.Count - 1)
            {
                issues.Add(new PipelineValidationIssue(
                    StepIndex: i,
                    Code: "Step.DeleteMustBeFinal",
                    Message: "Delete must be the final step in the pipeline.",
                    Severity: PipelineValidationSeverity.Blocking));
            }

            if (contract.SetsSourceExists.HasValue)
            {
                sourceExists = contract.SetsSourceExists.Value;
            }
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

    private static string? GetDestinationPath(ITransformStep step)
    {
        return step switch
        {
            CopyStep copyStep => copyStep.DestinationPath,
            MoveStep moveStep => moveStep.DestinationPath,
            _ => null,
        };
    }
}
