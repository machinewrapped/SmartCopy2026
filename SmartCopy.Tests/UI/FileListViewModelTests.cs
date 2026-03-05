using SmartCopy.Core.Filters;
using SmartCopy.Tests.TestInfrastructure;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.UI;

public sealed class FileListViewModelTests
{
    // ── Clear ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Clear_EmptiesVisibleFiles()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/music")
            .WithFile("/music/track.mp3", new byte[100]));
        var root = await provider.BuildDirectoryTree();
        var musicDir = root.Children.Single(n => n.Name == "music");

        var vm = new FileListViewModel();
        await vm.LoadFilesForNodeAsync(musicDir, FilterChain.Empty, FilterContext.LocalOnly);
        Assert.NotEmpty(vm.VisibleFiles);

        vm.Clear();

        Assert.Empty(vm.VisibleFiles);
    }

    // ── ClearIfUnder ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearIfUnder_ClearsWhenNodeIsCurrentDirectory()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/music")
            .WithFile("/music/track.mp3", new byte[100]));
        var root = await provider.BuildDirectoryTree();
        var musicDir = root.Children.Single(n => n.Name == "music");

        var vm = new FileListViewModel();
        await vm.LoadFilesForNodeAsync(musicDir, FilterChain.Empty, FilterContext.LocalOnly);

        vm.ClearIfUnder(musicDir);

        Assert.Empty(vm.VisibleFiles);
    }

    [Fact]
    public async Task ClearIfUnder_ClearsWhenNodeIsAncestorOfCurrentDirectory()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/parent")
            .WithDirectory("/parent/child")
            .WithFile("/parent/child/file.txt", new byte[10]));
        var root = await provider.BuildDirectoryTree();
        var parent = root.Children.Single(n => n.Name == "parent");
        var child = parent.Children.Single(n => n.Name == "child");

        var vm = new FileListViewModel();
        await vm.LoadFilesForNodeAsync(child, FilterChain.Empty, FilterContext.LocalOnly);
        Assert.NotEmpty(vm.VisibleFiles);

        vm.ClearIfUnder(parent);

        Assert.Empty(vm.VisibleFiles);
    }

    [Fact]
    public async Task ClearIfUnder_DoesNotClearWhenNodeIsUnrelated()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/dir1")
            .WithFile("/dir1/file.txt", new byte[10])
            .WithDirectory("/dir2"));
        var root = await provider.BuildDirectoryTree();
        var dir1 = root.Children.Single(n => n.Name == "dir1");
        var dir2 = root.Children.Single(n => n.Name == "dir2");

        var vm = new FileListViewModel();
        await vm.LoadFilesForNodeAsync(dir1, FilterChain.Empty, FilterContext.LocalOnly);
        Assert.NotEmpty(vm.VisibleFiles);

        vm.ClearIfUnder(dir2);

        Assert.NotEmpty(vm.VisibleFiles);
    }
}
