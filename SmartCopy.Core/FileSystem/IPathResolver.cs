namespace SmartCopy.Core.FileSystem;

public interface IPathResolver
{
    IFileSystemProvider? ResolveProvider(string path);

    /// <summary>
    /// Returns the filename portion of <paramref name="path"/> using the registered
    /// provider's path conventions.
    /// </summary>
    string GetFileName(string path)
    {
        var provider = ResolveProvider(path);
        return provider is not null ? provider.GetFileName(path) : string.Empty;
    }
}
