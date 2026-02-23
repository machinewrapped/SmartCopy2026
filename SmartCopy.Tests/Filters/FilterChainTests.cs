using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;

namespace SmartCopy.Tests.Filters;

public sealed class FilterChainTests
{
    [Fact]
    public async Task Apply_WithOnlyAndExcludeFilters_ReturnsExpectedNodes()
    {
        var nodes = new[]
        {
            CreateFile("a.mp3", 100),
            CreateFile("b.flac", 150),
            CreateFile("c.jpg", 20),
        };

        var chain = new FilterChain(
        [
            new ExtensionFilter(["mp3", "flac"], FilterMode.Only),
            new SizeRangeFilter(minBytes: 120, maxBytes: null, FilterMode.Exclude),
        ]);

        var result = (await chain.ApplyAsync(nodes)).ToList();

        Assert.Single(result);
        Assert.Equal("a.mp3", result[0].Name);
    }

    [Fact]
    public async Task ApplyToTree_SetsFilterResultAndExcludedByFilter()
    {
        var root = new FileSystemNode
        {
            Name = "root",
            FullPath = "/root",
            RelativePath = "root",
            IsDirectory = true,
        };
        var child = CreateFile("track.wav", 500, parent: root);
        root.Children.Add(child);

        var chain = new FilterChain([new ExtensionFilter(["mp3"], FilterMode.Only)]);
        await chain.ApplyToTreeAsync([root]);

        Assert.Equal(FilterResult.Excluded, child.FilterResult);
        Assert.Equal("Extension", child.ExcludedByFilter);
    }

    [Fact]
    public async Task OnlyThenAdd_CombinesAsUnion()
    {
        var nodes = new[]
        {
            CreateFile("a.mp3", 100),
            CreateFile("b.jpg", 50),
            CreateFile("c.txt", 30),
        };

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
        var nodes = new[]
        {
            CreateFile("a.mp3", 100),
            CreateFile("b.jpg", 50),
        };

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
        var nodes = new[]
        {
            CreateFile("a.mp3", 100),
            CreateFile("b.jpg", 50),
            CreateFile("c.txt", 30),
        };

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
        var nodes = new[]
        {
            CreateFile("a.mp3", 100),
            CreateFile("b.jpg", 50),
        };

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
        var nodes = new[]
        {
            CreateFile("a.mp3", 100),
            CreateFile("b.jpg", 50),
        };

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
        var dir = CreateDirectory("Music");
        var mp3 = CreateFile("track.mp3", 100, parent: dir);
        var txt = CreateFile("readme.txt", 50, parent: dir);
        dir.Children.Add(mp3);
        dir.Children.Add(txt);

        await new FilterChain([new ExtensionFilter(["mp3"], FilterMode.Only)])
            .ApplyToTreeAsync([dir]);

        Assert.Equal(FilterResult.Included, dir.FilterResult);
        Assert.Null(dir.ExcludedByFilter);
        Assert.Equal(FilterResult.Included, mp3.FilterResult);
        Assert.Equal(FilterResult.Excluded, txt.FilterResult);
    }

    [Fact]
    public async Task ApplyToTree_NestedDirectoriesNotExcludedByExtensionFilter()
    {
        var music = CreateDirectory("Music");
        var alt = CreateDirectory("Alternative", parent: music);
        var flac = CreateFile("track.flac", 1024, parent: alt);
        music.Children.Add(alt);
        alt.Children.Add(flac);

        await new FilterChain([new ExtensionFilter(["flac"], FilterMode.Only)])
            .ApplyToTreeAsync([music]);

        Assert.Equal(FilterResult.Included, music.FilterResult);
        Assert.Equal(FilterResult.Included, alt.FilterResult);
        Assert.Equal(FilterResult.Included, flac.FilterResult);
    }

    [Fact]
    public async Task ApplyToTree_MirrorFilter_ExcludesDirectoryWithNullProvider()
    {
        var dir = CreateDirectory("Music");
        var chain = new FilterChain([new MirrorFilter("/Mirror", MirrorCompareMode.NameOnly, FilterMode.Only)]);
        await chain.ApplyToTreeAsync([dir]); // null comparisonProvider → MirrorFilter returns false

        Assert.Equal(FilterResult.Excluded, dir.FilterResult);
    }

    private static FileSystemNode CreateFile(string name, long size, FileSystemNode? parent = null)
    {
        return new FileSystemNode
        {
            Name = name,
            FullPath = "/tmp/" + name,
            RelativePath = name,
            IsDirectory = false,
            Size = size,
            Parent = parent,
        };
    }

    private static FileSystemNode CreateDirectory(string name, FileSystemNode? parent = null) =>
        new()
        {
            Name = name,
            FullPath = "/tmp/" + name,
            RelativePath = name,
            IsDirectory = true,
            Parent = parent,
        };
}
