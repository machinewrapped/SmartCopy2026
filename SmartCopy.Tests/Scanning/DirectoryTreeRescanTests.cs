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
        File.Delete(Path.Combine(stonesPath, "gone.mp3"));

        await vm.RescanAsync(new ScanOptions());

        root = vm.RootNode!;
        var newStones = (DirectoryNode)root.FindNodeByPathSegments("stones")!;
        Assert.NotSame(stones, newStones);

        // A checked folder also checks content that appeared since the last scan
        Assert.Equal(CheckState.Checked, root.FindNodeByPathSegments("beatles", "yesterday.mp3")!.CheckState);
        Assert.False(((DirectoryNode)root.FindNodeByPathSegments("beatles")!).IsExpanded);

        Assert.Equal(CheckState.Checked, root.FindNodeByPathSegments("stones", "angie.mp3")!.CheckState);
        Assert.Equal(CheckState.Unchecked, root.FindNodeByPathSegments("stones", "wild horses.mp3")!.CheckState);
        Assert.Equal(CheckState.Indeterminate, newStones.CheckState);
        Assert.True(newStones.IsExpanded);
        Assert.Same(newStones, vm.SelectedNode);
        Assert.Equal(3, root.NumSelectedFiles);
    }
}
