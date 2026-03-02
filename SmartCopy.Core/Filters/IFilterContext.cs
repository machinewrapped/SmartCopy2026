using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Filters;

public interface IFilterContext
{
    IFileSystemProvider? ResolveProvider(string path);
}
