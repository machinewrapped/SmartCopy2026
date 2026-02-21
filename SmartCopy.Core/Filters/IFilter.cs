using SmartCopy.Core.FileSystem;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Filters;

public interface IFilter
{
    string Name { get; }
    FilterMode Mode { get; }
    bool IsEnabled { get; set; }
    FilterConfig Config { get; }
    string Summary { get; }
    string Description { get; }
    ValueTask<bool> MatchesAsync(
        FileSystemNode node,
        IFileSystemProvider? comparisonProvider,
        CancellationToken ct = default);
}
