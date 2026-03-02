using SmartCopy.Core.DirectoryTree;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Filters;

public interface IFilter
{
    string Name { get; }
    string TypeDisplayName { get; }
    FilterMode Mode { get; }
    bool IsEnabled { get; set; }
    string? CustomName { get; set; }
    FilterConfig Config { get; }
    bool AppliesToDirectories { get; }
    string Summary { get; }
    string Description { get; }

    ValueTask<bool> MatchesAsync(
        DirectoryTreeNode node,
        IFilterContext context,
        CancellationToken ct = default);
}
