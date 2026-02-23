using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
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
    public virtual string TypeDisplayName => Name;
    public FilterMode Mode { get; }
    public bool IsEnabled { get; set; }
    public string? CustomName { get; set; }
    public virtual bool AppliesToDirectories => false;
    public abstract string Summary { get; }
    public abstract string Description { get; }
    public abstract ValueTask<bool> MatchesAsync(
        FileSystemNode node,
        IFileSystemProvider? comparisonProvider,
        CancellationToken ct = default);

    public virtual FilterConfig Config => new(
        FilterType: Name,
        IsEnabled: IsEnabled,
        Mode: Mode.ToString(),
        Parameters: BuildParameters(),
        CustomName: CustomName);

    protected virtual JsonObject BuildParameters() => [];
}
