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
        await vm.ChangeRootAsync(rootPath, new ScanOptions {});

        var albums = vm.RootNode!.FindNodeByPathSegments(["albums"]) as DirectoryNode;
        var beatles = vm.RootNode.FindNodeByPathSegments(["albums", "beatles"]) as DirectoryNode;
        var song1 = vm.RootNode.FindNodeByPathSegments(["albums", "beatles", "song1.mp3"])!;

        Assert.NotNull(albums);
        Assert.NotNull(beatles);

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

        var refreshedBeatles = vm.RootNode.FindNodeByPathSegments(["albums", "beatles"]) as DirectoryNode;
        var refreshedSong1 = vm.RootNode.FindNodeByPathSegments(["albums", "beatles", "song1.mp3"])!;
        var refreshedSong2 = vm.RootNode.FindNodeByPathSegments(["albums", "beatles", "song2.mp3"])!;

        Assert.NotNull(refreshedBeatles);
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
        await vm.ChangeRootAsync(rootPath, new ScanOptions {});

        var song1 = vm.RootNode!.FindNodeByPathSegments(["albums", "beatles", "song1.mp3"])!;
        vm.SelectedNode = song1;

        File.Delete(songPath);

        vm.ApplyWatcherBatch(
            new DirectoryWatcherBatch(
                requiresFullRescan: false,
                deletions: [new DirectoryWatcherDeletion(["albums", "beatles", "song1.mp3"])],
                inserts: [],
                refreshes: []));

        var refreshedBeatles = vm.RootNode.FindNodeByPathSegments(["albums", "beatles"])!;
        Assert.Same(refreshedBeatles, vm.SelectedNode);
        Assert.Null(vm.RootNode.FindNodeByPathSegments(["albums", "beatles", "song1.mp3"]));
    }

    [Fact]
    public async Task ApplyWatcherBatch_KeepsAPathDeletedAndRecreatedInTheSameBatch()
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
        await vm.ChangeRootAsync(rootPath, new ScanOptions {});

        var oldBeatles = (DirectoryNode)vm.RootNode!.FindNodeByPathSegments(["albums", "beatles"])!;
        oldBeatles.CheckState = CheckState.Checked;
        oldBeatles.IsExpanded = true;
        vm.SelectedNode = oldBeatles;

        // The folder was replaced: the batch both deletes and inserts it
        var scanner = new DirectoryScanner(provider);
        vm.ApplyWatcherBatch(
            new DirectoryWatcherBatch(
                requiresFullRescan: false,
                deletions: [new DirectoryWatcherDeletion(["albums", "beatles"])],
                inserts:
                [
                    new DirectoryWatcherInsert(
                        ["albums", "beatles"],
                        await scanner.BuildScannedSubtreeAsync(
                            beatlesPath, new ScanOptions { LazyExpand = false, IncludeHidden = true }))
                ],
                refreshes: []));

        var beatles = vm.RootNode!.FindNodeByPathSegments(["albums", "beatles"]) as DirectoryNode;
        Assert.NotNull(beatles);
        Assert.NotSame(oldBeatles, beatles);
        Assert.False(beatles.IsMarkedForRemoval);
        Assert.Equal(CheckState.Checked, beatles.CheckState);
        Assert.True(beatles.IsExpanded);
        Assert.Same(beatles, vm.SelectedNode);
        Assert.Equal(CheckState.Checked, vm.RootNode.FindNodeByPathSegments(["albums", "beatles", "song1.mp3"])!.CheckState);
    }

    [Fact]
    public async Task ApplyWatcherBatch_KeepsTheCheckAndSelectionOfAReplacedFile()
    {
        using var temp = new TempDirectory();
        var rootPath = Path.Combine(temp.Path, "root");
        Directory.CreateDirectory(rootPath);
        await File.WriteAllTextAsync(Path.Combine(rootPath, "song1.mp3"), "a");
        await File.WriteAllTextAsync(Path.Combine(rootPath, "song2.mp3"), "a");

        var registry = new FileSystemProviderRegistry();
        var provider = new LocalFileSystemProvider(rootPath);
        registry.Register(provider);

        var vm = new DirectoryTreeViewModel(registry);
        await vm.ChangeRootAsync(rootPath, new ScanOptions {});

        // Only song1 is checked, so the parent is partly checked and can't supply the state
        var oldSong1 = vm.RootNode!.FindNodeByPathSegments(["song1.mp3"])!;
        oldSong1.CheckState = CheckState.Checked;
        vm.SelectedNode = oldSong1;

        var scanner = new DirectoryScanner(provider);
        vm.ApplyWatcherBatch(
            new DirectoryWatcherBatch(
                requiresFullRescan: false,
                deletions: [new DirectoryWatcherDeletion(["song1.mp3"])],
                inserts:
                [
                    new DirectoryWatcherInsert(
                        ["song1.mp3"],
                        await scanner.BuildScannedSubtreeAsync(
                            Path.Combine(rootPath, "song1.mp3"), new ScanOptions { LazyExpand = false, IncludeHidden = true }))
                ],
                refreshes: []));

        var song1 = vm.RootNode.FindNodeByPathSegments(["song1.mp3"])!;
        Assert.NotSame(oldSong1, song1);
        Assert.Equal(CheckState.Checked, song1.CheckState);
        Assert.Same(song1, vm.SelectedNode);
        Assert.Equal(CheckState.Indeterminate, vm.RootNode.CheckState);
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
        await vm.ChangeRootAsync(rootPath, new ScanOptions {});

        var albums = vm.RootNode!.FindNodeByPathSegments(["albums"])!;
        var beatles = vm.RootNode.FindNodeByPathSegments(["albums", "beatles"])!;
        vm.SelectedNode = beatles;

        Directory.Delete(beatlesPath, recursive: true);

        vm.ApplyWatcherBatch(
            new DirectoryWatcherBatch(
                requiresFullRescan: false,
                deletions: [new DirectoryWatcherDeletion(["albums", "beatles"])],
                inserts: [],
                refreshes: []));

        Assert.Null(vm.RootNode.FindNodeByPathSegments(["albums", "beatles"]));
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
        await vm.ChangeRootAsync(rootPath, new ScanOptions {});

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

        var beatles = vm.RootNode!.FindNodeByPathSegments(["albums", "beatles"]) as DirectoryNode;
        Assert.NotNull(beatles);
        Assert.Equal(["song-a.mp3", "song-b.mp3"], beatles.Files.Select(file => file.Name));
    }
}
