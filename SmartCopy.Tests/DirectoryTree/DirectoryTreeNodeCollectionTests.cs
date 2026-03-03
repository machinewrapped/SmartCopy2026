using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Tests.DirectoryTree;

public sealed class DirectoryTreeNodeCollectionTests
{
    private static DirectoryTreeNode MakeDir(string name, DirectoryTreeNode? parent = null)
        => new(new FileSystemNode { Name = name, FullPath = name, IsDirectory = true }, parent);

    private static DirectoryTreeNode MakeFile(string name, DirectoryTreeNode? parent = null)
        => new(new FileSystemNode { Name = name, FullPath = name, IsDirectory = false }, parent);

    [Fact]
    public void AddChild_MarksNodeDirty()
    {
        var root = MakeDir("root");
        root.BuildStats();
        Assert.False(root.IsDirty);

        root.Children.Add(MakeDir("sub", root));

        Assert.True(root.IsDirty);
    }

    [Fact]
    public void RemoveChild_MarksNodeDirty()
    {
        var root = MakeDir("root");
        var child = MakeDir("sub", root);
        root.Children.Add(child);
        root.BuildStats();
        Assert.False(root.IsDirty);

        root.Children.Remove(child);

        Assert.True(root.IsDirty);
    }

    [Fact]
    public void AddFile_MarksNodeDirty()
    {
        var root = MakeDir("root");
        root.BuildStats();
        Assert.False(root.IsDirty);

        root.Files.Add(MakeFile("a.txt", root));

        Assert.True(root.IsDirty);
    }

    [Fact]
    public void RemoveFile_MarksNodeDirty()
    {
        var root = MakeDir("root");
        var file = MakeFile("a.txt", root);
        root.Files.Add(file);
        root.BuildStats();
        Assert.False(root.IsDirty);

        root.Files.Remove(file);

        Assert.True(root.IsDirty);
    }

    [Fact]
    public void AddChild_PropagatesDirtyToAncestors()
    {
        var root = MakeDir("root");
        var parent = MakeDir("parent", root);
        root.Children.Add(parent);
        root.BuildStats();
        Assert.False(root.IsDirty);
        Assert.False(parent.IsDirty);

        parent.Children.Add(MakeDir("leaf", parent));

        Assert.True(parent.IsDirty);
        Assert.True(root.IsDirty);
    }

    [Fact]
    public void RemoveFile_PropagatesDirtyToRoot()
    {
        var root = MakeDir("root");
        var sub = MakeDir("sub", root);
        var file = MakeFile("a.txt", sub);
        root.Children.Add(sub);
        sub.Files.Add(file);
        root.BuildStats();
        Assert.False(root.IsDirty);
        Assert.False(sub.IsDirty);

        sub.Files.Remove(file);

        Assert.True(sub.IsDirty);
        Assert.True(root.IsDirty);
    }

    [Fact]
    public void CollectionChange_PropagatesDirtyThroughMultipleLevels()
    {
        var root = MakeDir("root");
        var level1 = MakeDir("level1", root);
        var level2 = MakeDir("level2", level1);
        root.Children.Add(level1);
        level1.Children.Add(level2);
        root.BuildStats();
        Assert.False(root.IsDirty);
        Assert.False(level1.IsDirty);
        Assert.False(level2.IsDirty);

        level2.Files.Add(MakeFile("deep.txt", level2));

        Assert.True(level2.IsDirty);
        Assert.True(level1.IsDirty);
        Assert.True(root.IsDirty);
    }
}
