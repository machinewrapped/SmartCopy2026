using System.Text.Json.Nodes;

namespace SmartCopy.Core.Pipeline;

public sealed record TransformStepConfig(
    StepKind StepType,
    JsonObject Parameters);

