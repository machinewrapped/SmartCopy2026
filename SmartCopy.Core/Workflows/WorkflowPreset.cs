namespace SmartCopy.Core.Workflows;

public sealed class WorkflowPreset
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public required WorkflowConfig Config { get; set; }
}
