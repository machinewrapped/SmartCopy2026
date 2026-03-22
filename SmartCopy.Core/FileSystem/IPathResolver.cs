namespace SmartCopy.Core.FileSystem;

public interface IPathResolver
{
    IFileSystemProvider? ResolveProvider(string path);
}
