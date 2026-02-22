using System.Collections.Generic;

namespace SmartCopy.Core.Filters;

public sealed class FilterPresetCollection
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Key = FilterConfig.FilterType ("Extension", "Wildcard", etc.).
    /// Only user-saved presets are stored; built-ins are merged at read time.
    /// </summary>
    public Dictionary<string, List<FilterPreset>> UserPresets { get; set; } = [];
}
