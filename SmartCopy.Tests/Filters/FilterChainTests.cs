using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;

namespace SmartCopy.Tests.Filters;

public sealed class FilterChainTests
{
    [Fact]
    public async Task Apply_WithIncludeAndExcludeFilters_ReturnsExpectedNodes()
    {
        var nodes = new[]
        {
            CreateFile("a.mp3", 100),
            CreateFile("b.flac", 150),
            CreateFile("c.jpg", 20),
        };

        var chain = new FilterChain(
        [
            new ExtensionFilter(["mp3", "flac"], FilterMode.Include),
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

        var chain = new FilterChain([new ExtensionFilter(["mp3"], FilterMode.Include)]);
        await chain.ApplyToTreeAsync([root]);

        Assert.Equal(FilterResult.Excluded, child.FilterResult);
        Assert.Equal("Extension", child.ExcludedByFilter);
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
}
