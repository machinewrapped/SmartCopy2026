using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Scanning;
using SmartCopy.Tests.TestInfrastructure;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.Scanning;

public sealed class DirectoryTreeWatcherCapabilityTests
{
    [Fact]
    public async Task ChangeRootAsync_StartsWatcher_ForWatchableLocalProvider()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "child"));

        var registry = new FileSystemProviderRegistry();
        var provider = new LocalFileSystemProvider(temp.Path);
        registry.Register(provider);

        var factory = new FakeDirectoryWatcherFactory();
        var vm = new DirectoryTreeViewModel(registry, factory);

        await vm.ChangeRootAsync(temp.Path);

        Assert.Equal(1, factory.CreateCalls);
        Assert.True(factory.CreatedWatcher?.Started);
    }

    [Fact]
    public async Task ChangeRootAsync_DoesNotStartWatcher_WhenProviderCannotWatch()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "child"));

        var registry = new FileSystemProviderRegistry();
        var local = new LocalFileSystemProvider(temp.Path);
        registry.Register(new CapabilityOverrideProvider(
            local,
            new ProviderCapabilities(CanSeek: true, CanAtomicMove: true, CanWatch: false, MaxPathLength: int.MaxValue)));

        var factory = new FakeDirectoryWatcherFactory();
        var vm = new DirectoryTreeViewModel(registry, factory);

        await vm.ChangeRootAsync(temp.Path);

        Assert.Equal(0, factory.CreateCalls);
    }

    [Fact]
    public async Task ChangeRootAsync_DoesNotStartWatcher_ForNonLocalProvider()
    {
        var memory = new MemoryFileSystemProvider();
        memory.SeedDirectory("/music");

        var factory = new FakeDirectoryWatcherFactory();
        var vm = new DirectoryTreeViewModel(memory.CreateRegistry(), factory);

        await vm.ChangeRootAsync(memory.RootPath);

        Assert.Equal(0, factory.CreateCalls);
    }

    private sealed class FakeDirectoryWatcherFactory : IDirectoryWatcherFactory
    {
        public int CreateCalls { get; private set; }
        public FakeDirectoryWatcher? CreatedWatcher { get; private set; }

        public IDirectoryWatcher Create(IFileSystemProvider provider, string path)
        {
            CreateCalls++;
            return CreatedWatcher = new FakeDirectoryWatcher();
        }
    }

    private sealed class FakeDirectoryWatcher : IDirectoryWatcher
    {
        public bool Started { get; private set; }

#pragma warning disable CS0067
        public event EventHandler<IReadOnlyCollection<string>>? ChangesBatched;
        public event EventHandler<Exception>? WatcherError;
#pragma warning restore CS0067

        public void Start() => Started = true;
        public void Stop() => Started = false;
        public void Dispose() { }
    }
}
