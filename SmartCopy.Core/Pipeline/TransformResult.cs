namespace SmartCopy.Core.Pipeline;

public readonly record struct TransformResult(
    bool Success,
    StepKind StepType,
    string? DestinationPath = null,
    long OutputBytes = 0,
    string? Message = null,
    string? SourcePath = null);
