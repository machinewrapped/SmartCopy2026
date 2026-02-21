using System.Text.Json.Nodes;

namespace SmartCopy.Core.Pipeline;

public sealed record TransformStepConfig(
    string StepType,
    JsonObject Parameters);

