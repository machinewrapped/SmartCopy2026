using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Filters;

public sealed class FilterChainTests
{
    [Fact]
    public async Task Apply_WithOnlyAndExcludeFilters_ReturnsExpectedNodes()
    {
        DirectoryTreeNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithSimulatedFile("/a.mp3", size: 100)
            .WithSimulatedFile("/b.flac", size: 150)
            .WithSimulatedFile("/c.jpg", size: 20));

        var chain = new FilterChain(
        [
            new ExtensionFilter(["mp3", "flac"], FilterMode.Only),
            new SizeRangeFilter(minBytes: 120, maxBytes: null, FilterMode.Exclude),
        ]);

        var result = (await chain.ApplyAsync(root.Files)).ToList();

        Assert.Single(result);
        Assert.Equal("a.mp3", result[0].Name);
    }

    [Fact]
    public async Task ApplyToTree_SetsFilterResultAndExcludedByFilter()
    {
        DirectoryTreeNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/src/")
            .WithSimulatedFile("/src/track.wav", size: 500));

        var dir = root.Children.Single(c => c.Name == "src");
        var child = dir.Files.Single(f => f.Name == "track.wav");

        var chain = new FilterChain([new ExtensionFilter(["mp3"], FilterMode.Only)]);
        await chain.ApplyToTreeAsync([root]);

        Assert.Equal(FilterResult.Excluded, child.FilterResult);
        Assert.Equal("Extension", child.ExcludedByFilter);
    }

    [Fact]
    public async Task OnlyThenAdd_CombinesAsUnion()
    {
        DirectoryTreeNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithSimulatedFile("/a.mp3", size: 100)
            .WithSimulatedFile("/b.jpg", size: 50)
            .WithSimulatedFile("/c.txt", size: 30));

        var nodes = root.Files;

        var chain = new FilterChain(
        [
            new ExtensionFilter(["mp3"], FilterMode.Only),
            new ExtensionFilter(["jpg"], FilterMode.Add),
        ]);

        var result = (await chain.ApplyAsync(nodes)).ToList();

        Assert.Equal(2, result.Count);
        Assert.Contains(result, n => n.Name == "a.mp3");
        Assert.Contains(result, n => n.Name == "b.jpg");
    }

    [Fact]
    public async Task ExcludeThenAdd_CancelsOut()
    {
        DirectoryTreeNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithSimulatedFile("/a.mp3", size: 100)
            .WithSimulatedFile("/b.jpg", size: 50));
        var nodes = root.Files;

        var chain = new FilterChain(
        [
            new ExtensionFilter(["mp3"], FilterMode.Exclude),
            new ExtensionFilter(["mp3"], FilterMode.Add),
        ]);

        var result = (await chain.ApplyAsync(nodes)).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task TwoOnlyFilters_CombineAsIntersection()
    {
        DirectoryTreeNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithSimulatedFile("/a.mp3", size: 100)
            .WithSimulatedFile("/b.jpg", size: 50)
            .WithSimulatedFile("/c.txt", size: 30));
        var nodes = root.Files;

        var chain = new FilterChain(
        [
            new ExtensionFilter(["mp3"], FilterMode.Only),
            new ExtensionFilter(["jpg"], FilterMode.Only),
        ]);

        var result = (await chain.ApplyAsync(nodes)).ToList();

        Assert.Empty(result);
    }

    [Fact]
    public async Task AddAlone_DoesNotRestrict()
    {
        DirectoryTreeNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithSimulatedFile("/root/a.mp3", size: 100)
            .WithSimulatedFile("/root/b.jpg", size: 50));
        var nodes = root.Children.First().Files;

        var chain = new FilterChain(
        [
            new ExtensionFilter(["mp3"], FilterMode.Add),
        ]);

        var result = (await chain.ApplyAsync(nodes)).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task OrderMatters_ExcludeAfterAdd_RemovesFiles()
    {
        DirectoryTreeNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithSimulatedFile("/root/a.mp3", size: 100)
            .WithSimulatedFile("/root/b.jpg", size: 50));
        var nodes = root.Children.First().Files;

        // Add mp3 (no-op since already in set), then exclude mp3
        var chain = new FilterChain(
        [
            new ExtensionFilter(["mp3"], FilterMode.Add),
            new ExtensionFilter(["mp3"], FilterMode.Exclude),
        ]);

        var result = (await chain.ApplyAsync(nodes)).ToList();

        Assert.Single(result);
        Assert.Equal("b.jpg", result[0].Name);
    }

    [Fact]
    public async Task ApplyToTree_DirectoryNotExcludedByExtensionFilter()
    {
        DirectoryTreeNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/root/Music")
            .WithSimulatedFile("/root/Music/track.mp3", size: 100)
            .WithSimulatedFile("/root/Music/readme.txt", size: 50));

        var dir = root.Children.First().Children.Single(c => c.Name == "Music");

        await new FilterChain([new ExtensionFilter(["mp3"], FilterMode.Only)])
            .ApplyToTreeAsync([dir]);

        Assert.Equal(FilterResult.Mixed, dir.FilterResult);
        Assert.Null(dir.ExcludedByFilter);

        var mp3 = dir.Files.Single(f => f.Name == "track.mp3");
        Assert.Equal(FilterResult.Included, mp3.FilterResult);

        var txt = dir.Files.Single(f => f.Name == "readme.txt");
        Assert.Equal(FilterResult.Excluded, txt.FilterResult);
    }

    [Fact]
    public async Task ApplyToTree_NestedDirectoriesNotExcludedByExtensionFilter()
    {
        DirectoryTreeNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/root/Music")
            .WithDirectory("/root/Music/Alternative")
            .WithSimulatedFile("/root/Music/Alternative/track.flac", size: 1024));
        var music = root.Children.Single(c => c.Name == "root").Children.Single(c => c.Name == "Music");
        var alt = music.Children.Single(c => c.Name == "Alternative");
        var flac = alt.Files.Single(f => f.Name == "track.flac");

        await new FilterChain([new ExtensionFilter(["flac"], FilterMode.Only)])
            .ApplyToTreeAsync([music]);

        Assert.Equal(FilterResult.Included, music.FilterResult);
        Assert.Equal(FilterResult.Included, alt.FilterResult);
        Assert.Equal(FilterResult.Included, flac.FilterResult);
    }

    [Fact]
    public async Task ApplyToTree_MirrorFilter_ExcludesDirectoryWhenMirrorPathMissing()
    {
        DirectoryTreeNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/source/Music")
            .WithSimulatedFile("/source/Music/song.mp3", size: 100));

        var dir = root
            .Children.Single(c => c.Name == "source")
            .Children.Single(c => c.Name == "Music");
        var chain = new FilterChain([new MirrorFilter("/mem/Mirror", MirrorCompareMode.NameOnly, FilterMode.Only)]);
        await chain.ApplyToTreeAsync([dir]);

        Assert.Equal(FilterResult.Excluded, dir.FilterResult);
    }

    [Fact]
    public async Task ApplyToTree_MirrorExclude_DirectoryWithMixedChildren_IsIncluded()
    {
        // Reproduces the bug: a directory that exists in the mirror but has some non-mirrored
        // children was being marked Excluded even though it has included content.
        MemoryFileSystemProvider provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/source/Alternative")
            .WithSimulatedFile("/source/Alternative/mirrored.flac", size: 1000)
            .WithSimulatedFile("/source/Alternative/new.flac", size: 800)
            .WithDirectory("/Mirror/Alternative")
            .WithSimulatedFile("/Mirror/Alternative/mirrored.flac", size: 1000));

        // "new.flac" is NOT in the mirror
        DirectoryTreeNode source = await MemoryFileSystemFixtures.BuildDirectoryTree(provider, "/source");

        var chain = new FilterChain([new MirrorFilter("/mem/Mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude)]);
        await chain.ApplyToTreeAsync([source], FilterContext.FromProvider(provider));

        // Directory has a non-mirrored file → must stay visible (Mixed: some included, some excluded)
        DirectoryTreeNode alt = source.Children.Single(c => c.Name == "Alternative");
        Assert.Equal(FilterResult.Mixed, alt.FilterResult);
        Assert.Null(alt.ExcludedByFilter);

        var mirrored = alt.Files.Single(f => f.Name == "mirrored.flac");
        Assert.Equal(FilterResult.Excluded, mirrored.FilterResult);

        var newFile = alt.Files.Single(f => f.Name == "new.flac");
        Assert.Equal(FilterResult.Included, newFile.FilterResult);
    }

    [Fact]
    public async Task ApplyToTree_MirrorExclude_DirectoryFullyMirrored_IsExcluded()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/source/Alternative")
            .WithSimulatedFile("/source/Alternative/song.flac", size: 1000)
            .WithDirectory("/Mirror/Alternative")
            .WithSimulatedFile("/Mirror/Alternative/song.flac", size: 1000));

        DirectoryTreeNode source = await MemoryFileSystemFixtures.BuildDirectoryTree(provider, "/source");

        var chain = new FilterChain([new MirrorFilter("/mem/Mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude)]);
        await chain.ApplyToTreeAsync([source], FilterContext.FromProvider(provider));

        // All content is mirrored → directory should be Excluded
        Assert.Equal(FilterResult.Excluded, source.FilterResult);
        var alt = source.Children.Single(c => c.Name == "Alternative");
        Assert.Equal(FilterResult.Excluded, alt.FilterResult);
        Assert.Equal(FilterResult.Excluded, alt.Files.Single(f => f.Name == "song.flac").FilterResult);
    }
}
