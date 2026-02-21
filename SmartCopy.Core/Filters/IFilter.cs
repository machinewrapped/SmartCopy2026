using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Filters;

public interface IFilter
{
    string Name { get; }
    FilterMode Mode { get; }
    bool IsEnabled { get; set; }
    FilterConfig Config { get; }
    string Summary { get; }
    string Description { get; }
    bool Matches(FileSystemNode node, IFileSystemProvider? comparisonProvider);
}

