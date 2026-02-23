namespace SmartCopy.Core.Pipeline.Validation;

public sealed record PipelineValidationIssue(
    int? StepIndex,
    string Code,
    string Message,
    PipelineValidationSeverity Severity);
