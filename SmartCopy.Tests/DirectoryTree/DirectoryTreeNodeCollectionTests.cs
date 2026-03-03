using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.DirectoryTree;

public sealed class DirectoryTreeNodeCollectionTests
{
    // Only used to construct new nodes being *added* to an existing tree.
    private static DirectoryTreeNode MakeDir(string name, DirectoryTreeNode? parent = null)
        => new(new FileSystemNode { Name = name, FullPath = name, IsDirectory = true }, parent);

    private static DirectoryTreeNode MakeFile(string name, DirectoryTreeNode? parent = null)
        => new(new FileSystemNode { Name = name, FullPath = name, IsDirectory = false }, parent);

    [Fact]
    public async Task AddChild_MarksNodeDirty()
    {
        var rootNode = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/root"));
        var root = rootNode.FindNodeByPathSegments(["root"]);
        Assert.NotNull(root);
        rootNode.BuildStats();
        Assert.False(root.IsDirty);

        root.Children.Add(MakeDir("sub", root));

        Assert.True(root.IsDirty);
    }

    [Fact]
    public async Task RemoveChild_MarksNodeDirty()
    {
        var rootNode = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/root")
            .WithDirectory("/root/sub"));
        var root = rootNode.FindNodeByPathSegments(["root"]);
        var sub = rootNode.FindNodeByPathSegments(["root", "sub"]);
        Assert.NotNull(root);
        Assert.NotNull(sub);
        rootNode.BuildStats();
        Assert.False(root.IsDirty);

        root.Children.Remove(sub);

        Assert.True(root.IsDirty);
    }

    [Fact]
    public async Task AddFile_MarksNodeDirty()
    {
        var rootNode = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/root"));
        var root = rootNode.FindNodeByPathSegments(["root"]);
        Assert.NotNull(root);
        rootNode.BuildStats();
        Assert.False(root.IsDirty);

        root.Files.Add(MakeFile("a.txt", root));

        Assert.True(root.IsDirty);
    }

    [Fact]
    public async Task RemoveFile_MarksNodeDirty()
    {
        var rootNode = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/root")
            .WithSimulatedFile("/root/a.txt", 512));
        var root = rootNode.FindNodeByPathSegments(["root"]);
        var file = rootNode.FindNodeByPathSegments(["root", "a.txt"]);
        Assert.NotNull(root);
        Assert.NotNull(file);
        rootNode.BuildStats();
        Assert.False(root.IsDirty);

        root.Files.Remove(file);

        Assert.True(root.IsDirty);
    }

    [Fact]
    public async Task AddChild_PropagatesDirtyToAncestors()
    {
        var rootNode = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/root")
            .WithDirectory("/root/parent"));
        var root = rootNode.FindNodeByPathSegments(["root"]);
        var parent = rootNode.FindNodeByPathSegments(["root", "parent"]);
        Assert.NotNull(root);
        Assert.NotNull(parent);
        rootNode.BuildStats();
        Assert.False(rootNode.IsDirty);
        Assert.False(parent.IsDirty);

        parent.Children.Add(MakeDir("leaf", parent));

        Assert.True(parent.IsDirty);
        Assert.True(root.IsDirty);
        Assert.True(rootNode.IsDirty);
    }

    [Fact]
    public async Task RemoveFile_PropagatesDirtyToRoot()
    {
        var rootNode = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/root")
            .WithDirectory("/root/sub")
            .WithSimulatedFile("/root/sub/a.txt", 512));
        var sub = rootNode.FindNodeByPathSegments(["root", "sub"]);
        var file = rootNode.FindNodeByPathSegments(["root", "sub", "a.txt"]);
        Assert.NotNull(sub);
        Assert.NotNull(file);
        rootNode.BuildStats();
        Assert.False(rootNode.IsDirty);
        Assert.False(sub.IsDirty);

        sub.Files.Remove(file);

        Assert.True(sub.IsDirty);
        Assert.True(rootNode.IsDirty);
    }

    [Fact]
    public async Task CollectionChange_PropagatesDirtyThroughMultipleLevels()
    {
        var rootNode = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/root")
            .WithDirectory("/root/level1")
            .WithDirectory("/root/level1/level2"));
        var level1 = rootNode.FindNodeByPathSegments(["root", "level1"]);
        var level2 = rootNode.FindNodeByPathSegments(["root", "level1", "level2"]);
        Assert.NotNull(level1);
        Assert.NotNull(level2);
        rootNode.BuildStats();
        Assert.False(rootNode.IsDirty);
        Assert.False(level1.IsDirty);
        Assert.False(level2.IsDirty);

        level2.Files.Add(MakeFile("deep.txt", level2));

        Assert.True(level2.IsDirty);
        Assert.True(level1.IsDirty);
        Assert.True(rootNode.IsDirty);
    }
}
