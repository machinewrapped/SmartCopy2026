using System;

namespace SmartCopy.Core.Filters;

public sealed class FilterPreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public FilterConfig Config { get; set; } = null!;
}
