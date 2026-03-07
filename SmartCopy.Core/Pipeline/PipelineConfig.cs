namespace SmartCopy.Core.Pipeline;

public sealed record PipelineConfig(
    string Name,
    string? Description,
    List<TransformStepConfig> Steps);



