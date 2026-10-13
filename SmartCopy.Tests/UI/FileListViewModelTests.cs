using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
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
        var musicDir = root.Children.Single(n => n.Name == "music") as DirectoryNode;
        Assert.NotNull(musicDir);

        var vm = new FileListViewModel();
        await vm.LoadFilesForNodeAsync(musicDir, FilterChain.Empty, new TestAppContext());
        Assert.NotEmpty(vm.VisibleFiles);

        vm.Clear();

        Assert.Empty(vm.VisibleFiles);
    }

    // ── LoadFilesForNodeAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task LoadFilesForNodeAsync_SupersededLoad_CompletesWithoutThrowing()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/music")
            .WithFile("/music/track.mp3", new byte[100]));
        var root = await provider.BuildDirectoryTree();
        var musicDir = root.Children.Single(n => n.Name == "music") as DirectoryNode;
        Assert.NotNull(musicDir);

        var vm = new FileListViewModel();
        var context = new TestAppContext();
        var firstLoad = vm.LoadFilesForNodeAsync(musicDir, new FilterChain([new BlockingFilter()]), context);

        await vm.LoadFilesForNodeAsync(musicDir, FilterChain.Empty, context);

        await firstLoad;
        Assert.NotEmpty(vm.VisibleFiles);
    }

    // ── ClearIfUnder ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearIfUnder_ClearsWhenNodeIsCurrentDirectory()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/music")
            .WithFile("/music/track.mp3", new byte[100]));
        var root = await provider.BuildDirectoryTree();
        var musicDir = root.Children.Single(n => n.Name == "music") as DirectoryNode;
        Assert.NotNull(musicDir);

        var vm = new FileListViewModel();
        await vm.LoadFilesForNodeAsync(musicDir, FilterChain.Empty, new TestAppContext());

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
        var parent = root.Children.Single(n => n.Name == "parent") as DirectoryNode;
        Assert.NotNull(parent);
        var child = parent.Children.Single(n => n.Name == "child") as DirectoryNode;
        Assert.NotNull(child);

        var vm = new FileListViewModel();
        await vm.LoadFilesForNodeAsync(child, FilterChain.Empty, new TestAppContext());
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
        var dir1 = root.Children.Single(n => n.Name == "dir1") as DirectoryNode;
        Assert.NotNull(dir1);
        var dir2 = root.Children.Single(n => n.Name == "dir2") as DirectoryNode;
        Assert.NotNull(dir2);

        var vm = new FileListViewModel();
        await vm.LoadFilesForNodeAsync(dir1, FilterChain.Empty, new TestAppContext());
        Assert.NotEmpty(vm.VisibleFiles);

        vm.ClearIfUnder(dir2);

        Assert.NotEmpty(vm.VisibleFiles);
    }

    [Fact]
    public async Task CurrentDirectoryFileCollectionChange_RefreshesVisibleFiles()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/music")
            .WithFile("/music/track.mp3", new byte[100]));
        var root = await provider.BuildDirectoryTree();
        var musicDir = root.Children.Single(n => n.Name == "music") as DirectoryNode;
        Assert.NotNull(musicDir);

        var vm = new FileListViewModel();
        await vm.LoadFilesForNodeAsync(musicDir, FilterChain.Empty, new TestAppContext());

        musicDir.Files.Add(new FileNode(
            new FileSystemNode
            {
                Name = "bonus.mp3",
                FullPath = "/music/bonus.mp3",
                IsDirectory = false,
                Size = 50,
                CreatedAt = DateTime.UtcNow,
                ModifiedAt = DateTime.UtcNow,
                Attributes = FileAttributes.Normal,
            },
            musicDir));

        Assert.Contains(vm.VisibleFiles, file => file.Name == "bonus.mp3");
    }

    /// <summary>Never returns until cancelled, like a slow mirror check against the destination.</summary>
    private sealed class BlockingFilter() : FilterBase("Blocking", FilterMode.Only)
    {
        public override string Summary => Name;
        public override string Description => Name;

        public override async ValueTask<bool> MatchesAsync(
            DirectoryTreeNode node, IPathResolver context, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return true;
        }
    }
}
