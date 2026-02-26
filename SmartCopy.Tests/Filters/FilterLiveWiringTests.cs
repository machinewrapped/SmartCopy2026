using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.Filters;

public sealed class FilterLiveWiringTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a MemoryFileSystemProvider with a single directory "/music" containing
    /// an .mp3 file and a .jpg file, plus returns the directory node needed by
    /// LoadFilesForNodeAsync.
    /// </summary>
    private static (MemoryFileSystemProvider fs, FileSystemNode dirNode) BuildMusicFs()
    {
        var fs = new MemoryFileSystemProvider();
        fs.SeedDirectory("/music");
        fs.SeedSimulatedFile("/music/track.mp3", 1024);
        fs.SeedSimulatedFile("/music/photo.jpg", 512);

        var dirNode = new FileSystemNode
        {
            Name = "music",
            FullPath = "/music",
            RelativePathSegments = ["music"],
            IsDirectory = true,
        };

        return (fs, dirNode);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExtensionFilter_ExcludesNonMatchingFiles()
    {
        var (fs, dirNode) = BuildMusicFs();
        var vm = new FileListViewModel(fs, "/music");

        // Include only mp3 — jpg should be excluded
        var chain = new FilterChain([new ExtensionFilter(["mp3"], FilterMode.Only)]);
        vm.UpdateChain(chain, null);

        await vm.LoadFilesForNodeAsync(dirNode);

        var jpg = vm.VisibleFiles.FirstOrDefault(f => f.Name == "photo.jpg");
        Assert.NotNull(jpg);
        Assert.Equal(FilterResult.Excluded, jpg.FilterResult);
    }

    [Fact]
    public async Task IncludeAndExcludeChain_CombinesCorrectly()
    {
        var fs = new MemoryFileSystemProvider();
        fs.SeedDirectory("/music");
        fs.SeedSimulatedFile("/music/small.mp3", 100);
        fs.SeedSimulatedFile("/music/large.mp3", 10_000_000);
        fs.SeedSimulatedFile("/music/photo.jpg", 200);

        var dirNode = new FileSystemNode
        {
            Name = "music",
            FullPath = "/music",
            RelativePathSegments = ["music"],
            IsDirectory = true,
        };

        var chain = new FilterChain(
        [
            new ExtensionFilter(["mp3"], FilterMode.Only),
            new SizeRangeFilter(minBytes: 1_000_000, maxBytes: null, FilterMode.Exclude),
        ]);

        var vm = new FileListViewModel(fs, "/music");
        vm.UpdateChain(chain, null);
        await vm.LoadFilesForNodeAsync(dirNode);

        var small = vm.VisibleFiles.First(f => f.Name == "small.mp3");
        var large = vm.VisibleFiles.First(f => f.Name == "large.mp3");
        var photo = vm.VisibleFiles.First(f => f.Name == "photo.jpg");

        Assert.Equal(FilterResult.Included, small.FilterResult);
        Assert.Equal(FilterResult.Excluded, large.FilterResult);
        Assert.Equal(FilterResult.Excluded, photo.FilterResult);
    }

    [Fact]
    public async Task ShowFilteredFiles_False_VisibleFilesOmitsExcluded()
    {
        var (fs, dirNode) = BuildMusicFs();
        var chain = new FilterChain([new ExtensionFilter(["mp3"], FilterMode.Only)]);

        var vm = new FileListViewModel(fs, "/music");
        vm.UpdateChain(chain, null);
        await vm.LoadFilesForNodeAsync(dirNode);

        vm.ShowFilteredFiles = false;

        Assert.All(vm.VisibleFiles, f => Assert.Equal(FilterResult.Included, f.FilterResult));
        Assert.DoesNotContain(vm.VisibleFiles, f => f.Name == "photo.jpg");
    }

    [Fact]
    public async Task ShowFilteredFiles_True_VisibleFilesIncludesAll()
    {
        var (fs, dirNode) = BuildMusicFs();
        var chain = new FilterChain([new ExtensionFilter(["mp3"], FilterMode.Only)]);

        var vm = new FileListViewModel(fs, "/music");
        vm.UpdateChain(chain, null);
        await vm.LoadFilesForNodeAsync(dirNode);

        // Default is true
        vm.ShowFilteredFiles = true;

        Assert.Equal(2, vm.VisibleFiles.Count);
    }

    [Fact]
    public async Task DisablingFilter_ResetsExcludedNodesToIncluded()
    {
        var (fs, dirNode) = BuildMusicFs();

        var activeFilter = new ExtensionFilter(["mp3"], FilterMode.Only);
        var activeChain = new FilterChain([activeFilter]);

        var vm = new FileListViewModel(fs, "/music");
        vm.UpdateChain(activeChain, null);
        await vm.LoadFilesForNodeAsync(dirNode);

        // Confirm jpg is excluded
        var jpg = vm.VisibleFiles.First(f => f.Name == "photo.jpg");
        Assert.Equal(FilterResult.Excluded, jpg.FilterResult);

        // Now replace the chain with a disabled version of the same filter
        var disabledFilter = new ExtensionFilter(["mp3"], FilterMode.Only, isEnabled: false);
        var disabledChain = new FilterChain([disabledFilter]);
        vm.UpdateChain(disabledChain, null);
        await vm.ReapplyFiltersAsync();

        // With filter disabled, every file should now be Included
        Assert.All(vm.VisibleFiles, f => Assert.Equal(FilterResult.Included, f.FilterResult));
    }

    [Fact]
    public async Task ReapplyFiltersAsync_UpdatesExistingFileListNodes()
    {
        var (fs, dirNode) = BuildMusicFs();

        var vm = new FileListViewModel(fs, "/music");
        // Load with no chain — all Included by default
        await vm.LoadFilesForNodeAsync(dirNode);

        Assert.All(vm.VisibleFiles, f => Assert.Equal(FilterResult.Included, f.FilterResult));

        // Now apply a chain that excludes jpg
        var chain = new FilterChain([new ExtensionFilter(["mp3"], FilterMode.Only)]);
        vm.UpdateChain(chain, null);
        await vm.ReapplyFiltersAsync();

        var jpg = vm.VisibleFiles.First(f => f.Name == "photo.jpg");
        Assert.Equal(FilterResult.Excluded, jpg.FilterResult);

        var mp3 = vm.VisibleFiles.First(f => f.Name == "track.mp3");
        Assert.Equal(FilterResult.Included, mp3.FilterResult);
    }
}
