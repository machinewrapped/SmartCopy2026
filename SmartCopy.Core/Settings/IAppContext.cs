using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Settings;

public interface IAppContext : IPathResolver
{
    AppSettings Settings { get; }
    IAppDataStore DataStore { get; }

    /// <summary>
    /// Registers a new file system provider so it can be resolved by path prefix.
    /// </summary>
    void Register(IFileSystemProvider provider);
}
