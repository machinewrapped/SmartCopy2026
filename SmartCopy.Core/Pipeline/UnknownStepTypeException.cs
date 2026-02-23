using System;

namespace SmartCopy.Core.Pipeline;

public sealed class UnknownStepTypeException(string stepType)
    : InvalidOperationException($"Unknown step type: {stepType}");
