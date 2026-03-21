using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Settings;

namespace SmartCopy.Tests;

public class TestAppContext : IAppContext
{
    public AppSettings Settings { get; }
    public IAppDataStore DataStore { get; }
    public FileSystemProviderRegistry ProviderRegistry { get; }

    public TestAppContext(AppSettings? settings = null, FileSystemProviderRegistry? providerRegistry = null)
    {
        Settings = settings ?? new AppSettings();
        DataStore = new TestAppDataStore();
        ProviderRegistry = providerRegistry ?? new FileSystemProviderRegistry();
    }

    public void Register(IFileSystemProvider provider)
    {
        ProviderRegistry.Register(provider);
    }

    public IFileSystemProvider? ResolveProvider(string path)
    {
        return ProviderRegistry.ResolveProvider(path);
    }

    public static TestAppContext FromProvider(IFileSystemProvider provider)
    {
        var registry = new FileSystemProviderRegistry();
        registry.Register(provider);
        return new TestAppContext(providerRegistry: registry);
    }
}
