using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Settings;

public sealed class SmartCopyAppContext : IAppContext, IPathResolver
{
    private readonly FileSystemProviderRegistry _providerRegistry;

    public AppSettings Settings { get; }
    public IAppDataStore DataStore { get; }

    public SmartCopyAppContext(AppSettings settings, IAppDataStore dataStore, FileSystemProviderRegistry? providerRegistry)
    {
        Settings = settings;
        DataStore = dataStore;
        _providerRegistry = providerRegistry;
    }

    public IFileSystemProvider? ResolveProvider(string path)
    {
        return _providerRegistry.ResolveProvider(path);
    }
}
