using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Scanning;
using SmartCopy.Tests.TestInfrastructure;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.Scanning;

public sealed class DirectoryTreeRescanTests
{
    [Fact]
    public async Task RescanAsync_KeepsCheckedExpandedAndSelectedStateForPathsThatStillExist()
    {
        using var temp = new TempDirectory();
        var rootPath = Path.Combine(temp.Path, "root");
        var beatlesPath = Path.Combine(rootPath, "beatles");
        var stonesPath = Path.Combine(rootPath, "stones");
        Directory.CreateDirectory(beatlesPath);
        Directory.CreateDirectory(stonesPath);
        await File.WriteAllTextAsync(Path.Combine(beatlesPath, "help.mp3"), "a");
        await File.WriteAllTextAsync(Path.Combine(stonesPath, "angie.mp3"), "a");
        await File.WriteAllTextAsync(Path.Combine(stonesPath, "wild horses.mp3"), "a");
        await File.WriteAllTextAsync(Path.Combine(stonesPath, "gone.mp3"), "a");

        var registry = new FileSystemProviderRegistry();
        registry.Register(new LocalFileSystemProvider(rootPath));
        var vm = new DirectoryTreeViewModel(registry);
        await vm.ChangeRootAsync(rootPath, new ScanOptions());

        var root = vm.RootNode!;
        var beatles = (DirectoryNode)root.FindNodeByPathSegments("beatles")!;
        var stones = (DirectoryNode)root.FindNodeByPathSegments("stones")!;
        beatles.CheckState = CheckState.Checked;
        beatles.IsExpanded = false;
        root.FindNodeByPathSegments("stones", "angie.mp3")!.CheckState = CheckState.Checked;
        root.FindNodeByPathSegments("stones", "gone.mp3")!.CheckState = CheckState.Checked;
        stones.IsExpanded = true;
        vm.SelectedNode = stones;

        await File.WriteAllTextAsync(Path.Combine(beatlesPath, "yesterday.mp3"), "a");
        // A checked file replaced by a folder of the same name must not pass its check on
        File.Delete(Path.Combine(stonesPath, "gone.mp3"));
        Directory.CreateDirectory(Path.Combine(stonesPath, "gone.mp3"));
        await File.WriteAllTextAsync(Path.Combine(stonesPath, "gone.mp3", "unrelated.mp3"), "a");

        await vm.RescanAsync(new ScanOptions());

        root = vm.RootNode!;
        var newStones = (DirectoryNode)root.FindNodeByPathSegments("stones")!;
        Assert.NotSame(stones, newStones);

        // A checked folder also checks content that appeared since the last scan
        Assert.Equal(CheckState.Checked, root.FindNodeByPathSegments("beatles", "yesterday.mp3")!.CheckState);
        Assert.False(((DirectoryNode)root.FindNodeByPathSegments("beatles")!).IsExpanded);

        Assert.Equal(CheckState.Checked, root.FindNodeByPathSegments("stones", "angie.mp3")!.CheckState);
        Assert.Equal(CheckState.Unchecked, root.FindNodeByPathSegments("stones", "wild horses.mp3")!.CheckState);
        Assert.Equal(CheckState.Unchecked, root.FindNodeByPathSegments("stones", "gone.mp3", "unrelated.mp3")!.CheckState);
        Assert.Equal(CheckState.Indeterminate, newStones.CheckState);
        Assert.True(newStones.IsExpanded);
        Assert.Same(newStones, vm.SelectedNode);
        Assert.Equal(3, root.NumSelectedFiles);
    }

    [Fact]
    public async Task ChangeRootAsync_SelectsTheNewRootRatherThanANodeFromTheOldTree()
    {
        using var temp = new TempDirectory();
        var moviesPath = Path.Combine(temp.Path, "movies");
        var musicPath = Path.Combine(temp.Path, "music");
        Directory.CreateDirectory(moviesPath);
        Directory.CreateDirectory(musicPath);

        var registry = new FileSystemProviderRegistry();
        registry.Register(new LocalFileSystemProvider(temp.Path));
        var vm = new DirectoryTreeViewModel(registry);
        await vm.ChangeRootAsync(moviesPath, new ScanOptions());
        Assert.Same(vm.RootNode, vm.SelectedNode);

        await vm.ChangeRootAsync(musicPath, new ScanOptions());

        Assert.Equal(musicPath, vm.RootNode!.FullPath);
        Assert.Same(vm.RootNode, vm.SelectedNode);
    }

    [Fact]
    public async Task RescanAsync_KeepsTheOldTreeWhenTheRescanDoesNotComplete()
    {
        using var temp = new TempDirectory();
        var rootPath = Path.Combine(temp.Path, "root");
        Directory.CreateDirectory(rootPath);
        await File.WriteAllTextAsync(Path.Combine(rootPath, "angie.mp3"), "a");

        var registry = new FileSystemProviderRegistry();
        registry.Register(new LocalFileSystemProvider(rootPath));
        var vm = new DirectoryTreeViewModel(registry);
        await vm.ChangeRootAsync(rootPath, new ScanOptions());

        var root = vm.RootNode!;
        var angie = root.FindNodeByPathSegments("angie.mp3")!;
        angie.CheckState = CheckState.Checked;
        vm.SelectedNode = angie;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => vm.RescanAsync(new ScanOptions(), new CancellationToken(canceled: true)));

        Assert.Same(root, vm.RootNode);
        Assert.Equal(CheckState.Checked, angie.CheckState);
        Assert.Same(angie, vm.SelectedNode);
        Assert.True(vm.IsLoaded);
    }

    [WindowsFact]
    public async Task RescanAsync_KeepsStateAcrossACaseOnlyRename()
    {
        using var temp = new TempDirectory();
        var rootPath = Path.Combine(temp.Path, "root");
        var stonesPath = Path.Combine(rootPath, "stones");
        Directory.CreateDirectory(stonesPath);
        await File.WriteAllTextAsync(Path.Combine(stonesPath, "angie.mp3"), "a");
        await File.WriteAllTextAsync(Path.Combine(stonesPath, "wild horses.mp3"), "a");

        var registry = new FileSystemProviderRegistry();
        registry.Register(new LocalFileSystemProvider(rootPath));
        var vm = new DirectoryTreeViewModel(registry);
        await vm.ChangeRootAsync(rootPath, new ScanOptions());

        var angie = vm.RootNode!.FindNodeByPathSegments("stones", "angie.mp3")!;
        angie.CheckState = CheckState.Checked;
        ((DirectoryNode)vm.RootNode.FindNodeByPathSegments("stones")!).IsExpanded = true;
        vm.SelectedNode = angie;

        File.Move(Path.Combine(stonesPath, "angie.mp3"), Path.Combine(stonesPath, "Angie.mp3"));
        Directory.Move(stonesPath, Path.Combine(rootPath, "Stones"));

        await vm.RescanAsync(new ScanOptions());

        var root = vm.RootNode!;
        var renamed = root.FindNodeByPathSegments("Stones", "Angie.mp3")!;
        Assert.Equal(CheckState.Checked, renamed.CheckState);
        Assert.True(((DirectoryNode)root.FindNodeByPathSegments("Stones")!).IsExpanded);
        Assert.Same(renamed, vm.SelectedNode);
    }
}
