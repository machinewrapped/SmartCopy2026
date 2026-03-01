using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.DirectoryTree;

public sealed class DirectoryTreeNodeSelectionTests
{
    [Fact]
    public async Task SettingDirectoryChecked_PropagatesToDescendants()
    {
        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/root")
            .WithDirectory("/root/albums")
            .WithFile("/root/albums/track1.mp3", "track1"u8)
            .WithFile("/root/cover.jpg", "cover"u8));

        root.CheckState = CheckState.Checked;

        var childDirectory = root.FindNodeByPathSegments(["root", "albums"]);
        var nestedFile = root.FindNodeByPathSegments(["root", "albums", "track1.mp3"]);
        var rootFile = root.FindNodeByPathSegments(["root", "cover.jpg"]);

        Assert.Equal(CheckState.Checked, root.CheckState);
        Assert.Equal(CheckState.Checked, childDirectory?.CheckState);
        Assert.Equal(CheckState.Checked, nestedFile?.CheckState);
        Assert.Equal(CheckState.Checked, rootFile?.CheckState);
    }

    [Fact]
    public async Task ChildTransitions_RecalculateParentCheckStateDeterministically()
    {
        var rootNode = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/root")
            .WithFile("/root/a.mp3", "a"u8)
            .WithFile("/root/b.mp3", "b"u8));
            
        var root = rootNode.FindNodeByPathSegments(["root"]);
        var fileA = rootNode.FindNodeByPathSegments(["root", "a.mp3"]);
        var fileB = rootNode.FindNodeByPathSegments(["root", "b.mp3"]);
        Assert.NotNull(root);
        Assert.NotNull(fileA);
        Assert.NotNull(fileB);

        fileA.CheckState = CheckState.Checked;
        Assert.Equal(CheckState.Indeterminate, root.CheckState);

        fileB.CheckState = CheckState.Checked;
        Assert.Equal(CheckState.Checked, root.CheckState);

        fileA.CheckState = CheckState.Unchecked;
        Assert.Equal(CheckState.Indeterminate, root.CheckState);

        fileB.CheckState = CheckState.Unchecked;
        Assert.Equal(CheckState.Unchecked, root.CheckState);
    }

    [Fact]
    public async Task IsSelected_RequiresCheckedAndIncluded()
    {
        var rootNode = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("root")
            .WithSimulatedFile("/root/song.flac", 1024));

        var node = rootNode.FindNodeByPathSegments(["root", "song.flac"]);

        Assert.NotNull(node);
        Assert.False(node.IsSelected);

        node.CheckState = CheckState.Checked;
        Assert.True(node.IsSelected);

        node.FilterResult = FilterResult.Excluded;
        Assert.False(node.IsSelected);
    }
}
