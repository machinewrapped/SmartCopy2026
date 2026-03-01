using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;
using SmartCopy.Tests.TestInfrastructure;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.Filters;

public sealed class FilterLiveWiringTests
{
    private static readonly string[] defaultFiles = ["track.mp3", "photo.jpg"];

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a MemoryFileSystemProvider with a single directory "/music" containing
    /// an .mp3 file and a .jpg file, plus returns the directory node needed by
    /// LoadFilesForNodeAsync.
    /// </summary>
    private static async Task<DirectoryTreeNode> BuildMusicDirNode(IEnumerable<string> files)
    {
        MemoryFileSystemProvider provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/music"));

        foreach (var file in files)
        {
            provider.SeedFile($"/music/{file}", content: new byte[100]);
        }

        DirectoryTreeNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider);

        return root.Children.Single(n => n.Name == "music");
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------


    [Fact]
    public async Task ExtensionFilter_ExcludesNonMatchingFiles()
    {
        var dirNode = await BuildMusicDirNode(defaultFiles);
        var vm = new FileListViewModel();

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

        DirectoryTreeNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(fs);

        var dirNode = root.Children.Single(n => n.Name == "music");

        var chain = new FilterChain(
        [
            new ExtensionFilter(["mp3"], FilterMode.Only),
            new SizeRangeFilter(minBytes: 1_000_000, maxBytes: null, FilterMode.Exclude),
        ]);

        var vm = new FileListViewModel();
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
        var dirNode = await BuildMusicDirNode(defaultFiles);
        var chain = new FilterChain([new ExtensionFilter(["mp3"], FilterMode.Only)]);

        var vm = new FileListViewModel();
        vm.UpdateChain(chain, null);
        await vm.LoadFilesForNodeAsync(dirNode);

        vm.ShowFilteredFiles = false;

        Assert.All(vm.VisibleFiles, f => Assert.Equal(FilterResult.Included, f.FilterResult));
        Assert.DoesNotContain(vm.VisibleFiles, f => f.Name == "photo.jpg");
    }

    [Fact]
    public async Task ShowFilteredFiles_True_VisibleFilesIncludesAll()
    {
        var dirNode = await BuildMusicDirNode(defaultFiles);
        var chain = new FilterChain([new ExtensionFilter(["mp3"], FilterMode.Only)]);

        var vm = new FileListViewModel();
        vm.UpdateChain(chain, null);
        await vm.LoadFilesForNodeAsync(dirNode);

        // Default is true
        vm.ShowFilteredFiles = true;

        Assert.Equal(2, vm.VisibleFiles.Count);
    }

    [Fact]
    public async Task DisablingFilter_ResetsExcludedNodesToIncluded()
    {
        var dirNode = await BuildMusicDirNode(defaultFiles);

        var activeFilter = new ExtensionFilter(["mp3"], FilterMode.Only);
        var activeChain = new FilterChain([activeFilter]);

        var vm = new FileListViewModel();
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
        var dirNode = await BuildMusicDirNode(defaultFiles);

        var vm = new FileListViewModel();
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

    [Fact]
    public async Task RemoveNode_RecalculatesParentExclusion()
    {
        MemoryFileSystemProvider fs = MemoryFileSystemFixtures.Create(f => f
             .WithDirectory("/root")
             .WithDirectory("/root/child1")
             .WithSimulatedFile("/root/child1/file1.txt", 100)
             .WithDirectory("/root/child2")
             .WithSimulatedFile("/root/child2/track.mp3", 100));

        var vm = new DirectoryTreeViewModel(fs, "/root");
        await vm.InitializeAsync();

        var chain = new FilterChain([new ExtensionFilter(["mp3"], FilterMode.Only)]);
        await vm.ApplyFiltersAsync(chain, null);

        var rootNode = vm.RootNodes.First(n => n.Name == "root");

        // At this point, child2 has the included mp3, so root should be Mixed (or Included if it has no files itself and all children match, but child1 is excluded)
        Assert.Equal(FilterResult.Mixed, rootNode.FilterResult);

        // Removing the only branch that contains included files should cause the root to become Excluded
        var removed = vm.RemoveNode("/root/child2");
        Assert.NotNull(removed);

        Assert.Equal(FilterResult.Excluded, rootNode.FilterResult);
    }
}
