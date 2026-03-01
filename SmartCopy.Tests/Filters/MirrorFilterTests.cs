using System.Drawing;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Filters;

public sealed class MirrorFilterTests
{
    // -------------------------------------------------------------------------
    // NameOnly mode
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MatchesAsync_NameOnly_ReturnsTrueWhenFileExistsInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/music/song.mp3", size: 500)
            .WithSimulatedFile("/mirror/song.mp3", size: 1000));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider, "/music");
        var node = root.FindNodeByPathSegments(["song.mp3"]);
        Assert.NotNull(node);

        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.True(await filter.MatchesAsync(node, provider));
    }

    [Fact]
    public async Task MatchesAsync_NameOnly_ReturnsFalseWhenFileNotInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/music/new.mp3", size: 500));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider);
        var node = root.FindNodeByPathSegments(["music", "new.mp3"]);
        Assert.NotNull(node);

        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(node, provider));
    }

    [Fact]
    public async Task MatchesAsync_NameOnly_NestedPath_ReturnsTrueWhenExistsInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/source/Alternative/Curve/song.mp3", size: 1000)
            .WithSimulatedFile("/mirror/Alternative/Curve/song.mp3", size: 1000));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider, "/source");
        var node = root.FindNodeByPathSegments(["Alternative", "Curve", "song.mp3"]);
        Assert.NotNull(node);

        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.True(await filter.MatchesAsync(node, provider));
    }

    [Fact]
    public async Task MatchesAsync_NameOnly_NestedPath_ReturnsFalseWhenNotInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/source/Alternative/Curve/song.mp3", size: 1000)
            // Sibling file exists in mirror but not the exact path
            .WithSimulatedFile("/mirror/Alternative/Curve/other.mp3", size: 1000));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider);
        var node = root.FindNodeByPathSegments(["source", "Alternative", "Curve", "song.mp3"]);
        Assert.NotNull(node);

        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(node, provider));
    }

    // -------------------------------------------------------------------------
    // NameAndSize mode
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MatchesAsync_NameAndSize_ReturnsTrueWhenNameAndSizeMatch()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/music/song.mp3", size: 1000)
            .WithSimulatedFile("/mirror/song.mp3", size: 1000));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider, "/music");
        var node = root.FindNodeByPathSegments(["song.mp3"]);
        Assert.NotNull(node);

        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameAndSize, FilterMode.Exclude);

        Assert.True(await filter.MatchesAsync(node, provider));
    }

    [Fact]
    public async Task MatchesAsync_NameAndSize_ReturnsFalseWhenSizeDiffers()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/music/song.mp3", size: 1000)
            .WithSimulatedFile("/mirror/song.mp3", size: 999)); // different size

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider);
        var node = root.FindNodeByPathSegments(["music", "song.mp3"]);
        Assert.NotNull(node);

        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameAndSize, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(node, provider));
    }

    [Fact]
    public async Task MatchesAsync_NameAndSize_ReturnsFalseWhenFileNotInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/music/song.mp3", size: 1000));
            // No file seeded in mirror

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider);
        var node = root.FindNodeByPathSegments(["music", "song.mp3"]);
        Assert.NotNull(node);

        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameAndSize, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(node, provider));
    }

    // -------------------------------------------------------------------------
    // Directory matching — a directory is "mirrored" only if it exists in the
    // mirror AND every direct file inside it also exists (and matches) there.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MatchesAsync_Directory_ReturnsTrueWhenEmptyDirectoryExistsInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/source/Alternative")
            .WithDirectory("/mirror/Alternative"));

        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude);

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider, "/source");
        var dir = root.FindNodeByPathSegments(["Alternative"]);
        Assert.NotNull(dir);

        // No files inside → vacuously all files are mirrored
        Assert.True(await filter.MatchesAsync(dir, provider));
    }

    [Fact]
    public async Task MatchesAsync_Directory_ReturnsFalseWhenDirectoryNotInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/music/song.mp3", size: 1000));
        // Nothing seeded under /mirror

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider);
        var dir = root.FindNodeByPathSegments(["music"]);
        Assert.NotNull(dir);

        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(dir, provider));
    }

    [Fact]
    public async Task MatchesAsync_Directory_ReturnsTrueWhenAllFilesInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/source/Alternative")
            .WithSimulatedFile("/source/Alternative/song.mp3", size: 1000)
            .WithDirectory("/mirror/Alternative")
            .WithSimulatedFile("/mirror/Alternative/song.mp3", size: 1000));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider, "/source");
        var dir = root.FindNodeByPathSegments(["Alternative"]);
        Assert.NotNull(dir);

        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.True(await filter.MatchesAsync(dir, provider));
    }

    [Fact]
    public async Task MatchesAsync_Directory_ReturnsFalseWhenSomeFilesNotInMirror()
    {   var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/Alternative")
            .WithSimulatedFile("/Alternative/old.mp3", size: 1000)
            .WithSimulatedFile("/Alternative/new.mp3", size: 800)
            .WithDirectory("/mirror/Alternative")
            .WithSimulatedFile("/mirror/Alternative/old.mp3", size: 1000));
            // "new.mp3" is NOT seeded in the mirror

        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude);

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider);
        var dir = root.FindNodeByPathSegments(["Alternative"]);
        Assert.NotNull(dir);

        Assert.False(await filter.MatchesAsync(dir, provider));
    }

    [Fact]
    public async Task MatchesAsync_Directory_NameAndSize_ReturnsFalseWhenFileSizeDiffers()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/Alternative")
            .WithSimulatedFile("/Alternative/song.mp3", size: 1000)
            .WithDirectory("/mirror/Alternative")
            .WithSimulatedFile("/mirror/Alternative/song.mp3", size: 999)); // different size

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider);
        var dir = root.FindNodeByPathSegments(["Alternative"]);
        Assert.NotNull(dir);

        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameAndSize, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(dir, provider));
    }

    // -------------------------------------------------------------------------
    // FilterChain integration
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FilterChain_MirrorExclude_ExcludesFilesAlreadyInMirror()
    {
        var fs = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/music")
            .WithSimulatedFile("/music/old.mp3", size: 500)
            .WithSimulatedFile("/music/new.mp3", size: 300)
            .WithDirectory("/mirror")
            .WithSimulatedFile("/mirror/old.mp3", size: 500));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(fs, "/music");
        var oldFile = root.FindNodeByPathSegments(["old.mp3"]);
        var newFile = root.FindNodeByPathSegments(["new.mp3"]);
        Assert.NotNull(oldFile);
        Assert.NotNull(newFile);

        var chain = new FilterChain([new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude)]);
        var result = (await chain.ApplyAsync([oldFile, newFile], comparisonProvider: fs)).ToList();

        Assert.Single(result);
        Assert.Equal("new.mp3", result[0].Name);
    }

    [Fact]
    public async Task FilterChain_MirrorExclude_NestedFiles_ExcludesOnlyMirroredOnes()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/source/Alternative")
            .WithSimulatedFile("/source/Alternative/Curve/song.mp3", size: 1000)
            .WithSimulatedFile("/source/Alternative/Algiers/pain.mp3", size: 800)
            .WithDirectory("/mirror/Alternative")
            .WithSimulatedFile("/mirror/Alternative/Curve/song.mp3", size: 1000));
            // "Algiers/pain.mp3" is NOT seeded in the mirror

        // Mirror contains the Curve album but not Algiers
        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider, "/source");

        var curveSong = root.FindNodeByPathSegments(["Alternative", "Curve", "song.mp3"]);
        var algiersSong = root.FindNodeByPathSegments(["Alternative", "Algiers", "pain.mp3"]);
        Assert.NotNull(curveSong);
        Assert.NotNull(algiersSong);

        var chain = new FilterChain([new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude)]);
        var result = (await chain.ApplyAsync([curveSong, algiersSong], comparisonProvider: provider)).ToList();

        Assert.Single(result);
        Assert.Equal("pain.mp3", result[0].Name);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static FileSystemNode MakeFile(string name, string relativePath, long size) =>
        new()
        {
            Name = name,
            FullPath = "/source/" + relativePath,
            PathSegments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries),
            IsDirectory = false,
            Size = size,
        };

    private static FileSystemNode MakeDirectory(string name, string relativePath) =>
        new()
        {
            Name = name,
            FullPath = "/source/" + relativePath,
            PathSegments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries),
            IsDirectory = true,
        };
}
