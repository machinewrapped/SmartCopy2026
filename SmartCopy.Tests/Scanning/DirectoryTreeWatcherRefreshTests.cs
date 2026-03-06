using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Scanning;
using SmartCopy.Tests.TestInfrastructure;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.Scanning;

public sealed class DirectoryTreeWatcherRefreshTests
{
    [Fact]
    public async Task ApplyWatcherBatch_PreservesCheckedExpandedAndSelectedState()
    {
        using var temp = new TempDirectory();
        var rootPath = Path.Combine(temp.Path, "root");
        var beatlesPath = Path.Combine(rootPath, "albums", "beatles");
        Directory.CreateDirectory(beatlesPath);
        await File.WriteAllTextAsync(Path.Combine(beatlesPath, "song1.mp3"), "a");

        var registry = new FileSystemProviderRegistry();
        var provider = new LocalFileSystemProvider(rootPath);
        registry.Register(provider);

        var vm = new DirectoryTreeViewModel(registry);
        await vm.ChangeRootAsync(rootPath);

        var albums = vm.RootNode!.FindNodeByPathSegments("albums")!;
        var beatles = vm.RootNode.FindNodeByPathSegments("albums", "beatles")!;
        var song1 = vm.RootNode.FindNodeByPathSegments("albums", "beatles", "song1.mp3")!;

        albums.CheckState = CheckState.Checked;
        beatles.IsExpanded = true;
        vm.SelectedNode = song1;

        await File.WriteAllTextAsync(Path.Combine(beatlesPath, "song2.mp3"), "b");

        var scanner = new DirectoryScanner(provider);
        vm.ApplyWatcherBatch(
            new DirectoryWatcherBatch(
                requiresFullRescan: false,
                deletions: [],
                inserts:
                [
                    new DirectoryWatcherInsert(
                        ["albums", "beatles", "song2.mp3"],
                        await scanner.BuildScannedSubtreeAsync(
                            Path.Combine(beatlesPath, "song2.mp3"),
                            new ScanOptions { LazyExpand = false, IncludeHidden = true }))
                ],
                refreshes: []));

        var refreshedBeatles = vm.RootNode.FindNodeByPathSegments("albums", "beatles")!;
        var refreshedSong1 = vm.RootNode.FindNodeByPathSegments("albums", "beatles", "song1.mp3")!;
        var refreshedSong2 = vm.RootNode.FindNodeByPathSegments("albums", "beatles", "song2.mp3")!;

        Assert.Equal(CheckState.Checked, refreshedSong1.CheckState);
        Assert.True(refreshedBeatles.IsExpanded);
        Assert.Same(refreshedSong1, vm.SelectedNode);
        Assert.NotNull(refreshedSong2);
    }

    [Fact]
    public async Task ApplyWatcherBatch_ReassignsSelectedNodeWhenSelectedChildIsDeleted()
    {
        using var temp = new TempDirectory();
        var rootPath = Path.Combine(temp.Path, "root");
        var beatlesPath = Path.Combine(rootPath, "albums", "beatles");
        var songPath = Path.Combine(beatlesPath, "song1.mp3");
        Directory.CreateDirectory(beatlesPath);
        await File.WriteAllTextAsync(songPath, "a");

        var registry = new FileSystemProviderRegistry();
        var provider = new LocalFileSystemProvider(rootPath);
        registry.Register(provider);

        var vm = new DirectoryTreeViewModel(registry);
        await vm.ChangeRootAsync(rootPath);

        var song1 = vm.RootNode!.FindNodeByPathSegments("albums", "beatles", "song1.mp3")!;
        vm.SelectedNode = song1;

        File.Delete(songPath);

        vm.ApplyWatcherBatch(
            new DirectoryWatcherBatch(
                requiresFullRescan: false,
                deletions: [new DirectoryWatcherDeletion(["albums", "beatles", "song1.mp3"])],
                inserts: [],
                refreshes: []));

        var refreshedBeatles = vm.RootNode.FindNodeByPathSegments("albums", "beatles")!;
        Assert.Same(refreshedBeatles, vm.SelectedNode);
        Assert.Null(vm.RootNode.FindNodeByPathSegments("albums", "beatles", "song1.mp3"));
    }

    [Fact]
    public async Task ApplyWatcherBatch_RemovesDeletedSubtreeAndSelectsParent()
    {
        using var temp = new TempDirectory();
        var rootPath = Path.Combine(temp.Path, "root");
        var beatlesPath = Path.Combine(rootPath, "albums", "beatles");
        Directory.CreateDirectory(beatlesPath);
        await File.WriteAllTextAsync(Path.Combine(beatlesPath, "song1.mp3"), "a");

        var registry = new FileSystemProviderRegistry();
        var provider = new LocalFileSystemProvider(rootPath);
        registry.Register(provider);

        var vm = new DirectoryTreeViewModel(registry);
        await vm.ChangeRootAsync(rootPath);

        var albums = vm.RootNode!.FindNodeByPathSegments("albums")!;
        var beatles = vm.RootNode.FindNodeByPathSegments("albums", "beatles")!;
        vm.SelectedNode = beatles;

        Directory.Delete(beatlesPath, recursive: true);

        vm.ApplyWatcherBatch(
            new DirectoryWatcherBatch(
                requiresFullRescan: false,
                deletions: [new DirectoryWatcherDeletion(["albums", "beatles"])],
                inserts: [],
                refreshes: []));

        Assert.Null(vm.RootNode.FindNodeByPathSegments("albums", "beatles"));
        Assert.Same(albums, vm.SelectedNode);
    }

    [Fact]
    public async Task ApplyWatcherBatch_InsertsNewNodesAlphabetically()
    {
        using var temp = new TempDirectory();
        var rootPath = Path.Combine(temp.Path, "root");
        var beatlesPath = Path.Combine(rootPath, "albums", "beatles");
        Directory.CreateDirectory(beatlesPath);
        await File.WriteAllTextAsync(Path.Combine(beatlesPath, "song-b.mp3"), "b");

        var registry = new FileSystemProviderRegistry();
        var provider = new LocalFileSystemProvider(rootPath);
        registry.Register(provider);

        var vm = new DirectoryTreeViewModel(registry);
        await vm.ChangeRootAsync(rootPath);

        await File.WriteAllTextAsync(Path.Combine(beatlesPath, "song-a.mp3"), "a");

        var scanner = new DirectoryScanner(provider);
        vm.ApplyWatcherBatch(
            new DirectoryWatcherBatch(
                requiresFullRescan: false,
                deletions: [],
                inserts:
                [
                    new DirectoryWatcherInsert(
                        ["albums", "beatles", "song-a.mp3"],
                        await scanner.BuildScannedSubtreeAsync(
                            Path.Combine(beatlesPath, "song-a.mp3"),
                            new ScanOptions { LazyExpand = false, IncludeHidden = true }))
                ],
                refreshes: []));

        var beatles = vm.RootNode!.FindNodeByPathSegments("albums", "beatles")!;
        Assert.Equal(["song-a.mp3", "song-b.mp3"], beatles.Files.Select(file => file.Name));
    }
}
