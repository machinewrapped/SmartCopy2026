using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Scanning;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Scanning;

public sealed class WatcherRefreshPlannerTests
{
    [Fact]
    public async Task CreatePlan_IgnoresPathsOutsideCurrentRoot()
    {
        using var temp = new TempDirectory();
        var rootPath = Path.Combine(temp.Path, "root");
        var outsidePath = Path.Combine(temp.Path, "outside");
        Directory.CreateDirectory(Path.Combine(rootPath, "child"));
        Directory.CreateDirectory(outsidePath);

        var rootNode = await BuildRootNode(rootPath);

        var provider = new LocalFileSystemProvider(rootPath);
        var plan = WatcherRefreshPlanner.CreatePlan(provider, rootNode, [Path.Combine(outsidePath, "file.txt")]);

        Assert.False(plan.RequiresFullRescan);
        Assert.Empty(plan.RefreshTargets);
    }

    [Fact]
    public async Task CreatePlan_CollapsesNestedPathsToShallowestExistingAncestor()
    {
        using var temp = new TempDirectory();
        var rootPath = Path.Combine(temp.Path, "root");
        Directory.CreateDirectory(Path.Combine(rootPath, "albums", "beatles"));

        var rootNode = await BuildRootNode(rootPath);

        var provider = new LocalFileSystemProvider(rootPath);
        var plan = WatcherRefreshPlanner.CreatePlan(
            provider,
            rootNode,
            [
                Path.Combine(rootPath, "albums", "beatles", "song.mp3"),
                Path.Combine(rootPath, "albums", "beatles", "cover.jpg"),
                Path.Combine(rootPath, "albums")
            ]);

        Assert.False(plan.RequiresFullRescan);
        Assert.Equal(["albums"], plan.RefreshTargets.Select(t => t.CanonicalRelativePath));
    }

    [Fact]
    public async Task CreatePlan_MapsNewNestedChildToNearestExistingAncestor()
    {
        using var temp = new TempDirectory();
        var rootPath = Path.Combine(temp.Path, "root");
        Directory.CreateDirectory(Path.Combine(rootPath, "albums"));

        var rootNode = await BuildRootNode(rootPath);

        var provider = new LocalFileSystemProvider(rootPath);
        var plan = WatcherRefreshPlanner.CreatePlan(
            provider,
            rootNode,
            [Path.Combine(rootPath, "albums", "new-folder", "new-song.mp3")]);

        Assert.False(plan.RequiresFullRescan);
        Assert.Equal(["albums"], plan.RefreshTargets.Select(t => t.CanonicalRelativePath));
    }

    [Fact]
    public async Task CreatePlan_ReturnsRootWhenRootPathChanges()
    {
        using var temp = new TempDirectory();
        var rootPath = Path.Combine(temp.Path, "root");
        Directory.CreateDirectory(Path.Combine(rootPath, "albums"));

        var rootNode = await BuildRootNode(rootPath);

        var provider = new LocalFileSystemProvider(rootPath);
        var plan = WatcherRefreshPlanner.CreatePlan(provider, rootNode, [rootPath]);

        Assert.False(plan.RequiresFullRescan);
        Assert.Equal([""], plan.RefreshTargets.Select(t => t.CanonicalRelativePath));
    }

    private static async Task<DirectoryTreeNode> BuildRootNode(string rootPath)
    {
        var provider = new LocalFileSystemProvider(rootPath);
        var scanner = new DirectoryScanner(provider);
        DirectoryTreeNode? rootNode = null;

        await foreach (var node in scanner.ScanAsync(rootPath, new ScanOptions { IncludeHidden = true }))
        {
            rootNode ??= node;
        }

        return rootNode!;
    }
}
