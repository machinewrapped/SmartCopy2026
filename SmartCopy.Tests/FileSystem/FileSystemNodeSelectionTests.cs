using SmartCopy.Core.FileSystem;

namespace SmartCopy.Tests.FileSystem;

public sealed class FileSystemNodeSelectionTests
{
    [Fact]
    public void SettingDirectoryChecked_PropagatesToDescendants()
    {
        var root = CreateDirectory("root", "/root", "root");
        var childDirectory = CreateDirectory("albums", "/root/albums", "root/albums", root);
        var nestedFile = CreateFile("track1.mp3", "/root/albums/track1.mp3", "root/albums/track1.mp3", childDirectory);
        var rootFile = CreateFile("cover.jpg", "/root/cover.jpg", "root/cover.jpg", root);

        root.Children.Add(childDirectory);
        childDirectory.Files.Add(nestedFile);
        root.Files.Add(rootFile);

        root.CheckState = CheckState.Checked;

        Assert.Equal(CheckState.Checked, root.CheckState);
        Assert.Equal(CheckState.Checked, childDirectory.CheckState);
        Assert.Equal(CheckState.Checked, nestedFile.CheckState);
        Assert.Equal(CheckState.Checked, rootFile.CheckState);
    }

    [Fact]
    public void ChildTransitions_RecalculateParentCheckStateDeterministically()
    {
        var root = CreateDirectory("root", "/root", "root");
        var fileA = CreateFile("a.mp3", "/root/a.mp3", "root/a.mp3", root);
        var fileB = CreateFile("b.mp3", "/root/b.mp3", "root/b.mp3", root);
        root.Files.Add(fileA);
        root.Files.Add(fileB);

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
    public void IsSelected_RequiresCheckedAndIncluded()
    {
        var node = CreateFile("song.flac", "/root/song.flac", "root/song.flac");

        Assert.False(node.IsSelected);

        node.CheckState = CheckState.Checked;
        Assert.True(node.IsSelected);

        node.FilterResult = FilterResult.Excluded;
        Assert.False(node.IsSelected);
    }

    private static FileSystemNode CreateDirectory(string name, string fullPath, string relativePath, FileSystemNode? parent = null)
    {
        return new FileSystemNode
        {
            Name = name,
            FullPath = fullPath,
            RelativePath = relativePath,
            IsDirectory = true,
            Parent = parent,
        };
    }

    private static FileSystemNode CreateFile(string name, string fullPath, string relativePath, FileSystemNode? parent = null)
    {
        return new FileSystemNode
        {
            Name = name,
            FullPath = fullPath,
            RelativePath = relativePath,
            IsDirectory = false,
            Parent = parent,
        };
    }
}
