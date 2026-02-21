using System.Text.Json.Nodes;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Filters;

public abstract class FilterBase : IFilter
{
    protected FilterBase(string name, FilterMode mode, bool isEnabled = true)
    {
        Name = name;
        Mode = mode;
        IsEnabled = isEnabled;
    }

    public string Name { get; }
    public FilterMode Mode { get; }
    public bool IsEnabled { get; set; }
    public abstract string Summary { get; }
    public abstract string Description { get; }
    public abstract bool Matches(FileSystemNode node, IFileSystemProvider? comparisonProvider);

    public virtual FilterConfig Config => new(
        FilterType: Name,
        IsEnabled: IsEnabled,
        Mode: Mode.ToString(),
        Parameters: BuildParameters());

    protected virtual JsonObject BuildParameters() => [];
}

