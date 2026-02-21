namespace SmartCopy.Core.Pipeline;

public readonly record struct TransformResult(
    bool Success,
    string StepType,
    string? DestinationPath = null,
    long OutputBytes = 0,
    string? Message = null);

