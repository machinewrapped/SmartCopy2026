using System;

namespace SmartCopy.Core.Pipeline;

public sealed class UnknownStepTypeException(StepKind stepType)
    : InvalidOperationException($"Unknown step type: {stepType}");
