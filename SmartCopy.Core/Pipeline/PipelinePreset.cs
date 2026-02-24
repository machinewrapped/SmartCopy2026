namespace SmartCopy.Core.Pipeline;

public sealed class PipelinePreset
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public required PipelineConfig Config { get; set; }
}
