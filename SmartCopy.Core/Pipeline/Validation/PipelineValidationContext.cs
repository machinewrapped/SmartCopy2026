namespace SmartCopy.Core.Pipeline.Validation;

public sealed record PipelineValidationContext(
    bool HasSelectedIncludedInputs = true);
