namespace SmartCopy.Core.Filters;

public sealed class FilterChainPreset
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public required FilterChainConfig Config { get; set; }
}
