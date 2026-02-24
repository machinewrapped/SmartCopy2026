using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;

namespace SmartCopy.Tests.Filters;

public sealed class MirrorFilterTests
{
    // -------------------------------------------------------------------------
    // NameOnly mode
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MatchesAsync_NameOnly_ReturnsTrueWhenFileExistsInMirror()
    {
        var fs = new MemoryFileSystemProvider();
        fs.SeedSimulatedFile("/mirror/song.mp3", size: 1000);

        // Size intentionally differs — NameOnly should not care
        var node = MakeFile("song.mp3", relativePath: "song.mp3", size: 500);
        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.True(await filter.MatchesAsync(node, fs));
    }

    [Fact]
    public async Task MatchesAsync_NameOnly_ReturnsFalseWhenFileNotInMirror()
    {
        var fs = new MemoryFileSystemProvider();
        // Nothing seeded under /mirror

        var node = MakeFile("new.mp3", relativePath: "new.mp3", size: 500);
        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(node, fs));
    }

    [Fact]
    public async Task MatchesAsync_NameOnly_NestedPath_ReturnsTrueWhenExistsInMirror()
    {
        var fs = new MemoryFileSystemProvider();
        fs.SeedSimulatedFile("/mirror/Alternative/Curve/song.mp3", size: 1000);

        var node = MakeFile("song.mp3", relativePath: "Alternative/Curve/song.mp3", size: 1000);
        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.True(await filter.MatchesAsync(node, fs));
    }

    [Fact]
    public async Task MatchesAsync_NameOnly_NestedPath_ReturnsFalseWhenNotInMirror()
    {
        var fs = new MemoryFileSystemProvider();
        // Sibling folder exists, but not the exact path
        fs.SeedSimulatedFile("/mirror/Alternative/Algiers/other.mp3", size: 100);

        var node = MakeFile("song.mp3", relativePath: "Alternative/Curve/song.mp3", size: 1000);
        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(node, fs));
    }

    // -------------------------------------------------------------------------
    // NameAndSize mode
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MatchesAsync_NameAndSize_ReturnsTrueWhenNameAndSizeMatch()
    {
        var fs = new MemoryFileSystemProvider();
        fs.SeedSimulatedFile("/mirror/song.mp3", size: 1000);

        var node = MakeFile("song.mp3", relativePath: "song.mp3", size: 1000);
        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameAndSize, FilterMode.Exclude);

        Assert.True(await filter.MatchesAsync(node, fs));
    }

    [Fact]
    public async Task MatchesAsync_NameAndSize_ReturnsFalseWhenSizeDiffers()
    {
        var fs = new MemoryFileSystemProvider();
        fs.SeedSimulatedFile("/mirror/song.mp3", size: 1000);

        var node = MakeFile("song.mp3", relativePath: "song.mp3", size: 999);
        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameAndSize, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(node, fs));
    }

    [Fact]
    public async Task MatchesAsync_NameAndSize_ReturnsFalseWhenFileNotInMirror()
    {
        var fs = new MemoryFileSystemProvider();

        var node = MakeFile("song.mp3", relativePath: "song.mp3", size: 1000);
        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameAndSize, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(node, fs));
    }

    // -------------------------------------------------------------------------
    // Directory matching (always NameOnly regardless of compareMode)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MatchesAsync_Directory_ReturnsTrueWhenDirectoryExistsInMirror()
    {
        var fs = new MemoryFileSystemProvider();
        fs.SeedDirectory("/mirror/Alternative");

        var dir = MakeDirectory("Alternative", relativePath: "Alternative");
        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.True(await filter.MatchesAsync(dir, fs));
    }

    [Fact]
    public async Task MatchesAsync_Directory_ReturnsFalseWhenDirectoryNotInMirror()
    {
        var fs = new MemoryFileSystemProvider();
        // Nothing seeded under /mirror

        var dir = MakeDirectory("NewBand", relativePath: "NewBand");
        var filter = new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(dir, fs));
    }

    // -------------------------------------------------------------------------
    // FilterChain integration
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FilterChain_MirrorExclude_ExcludesFilesAlreadyInMirror()
    {
        var fs = new MemoryFileSystemProvider();
        fs.SeedSimulatedFile("/mirror/old.mp3", size: 500);

        var oldFile = MakeFile("old.mp3", relativePath: "old.mp3", size: 500);
        var newFile = MakeFile("new.mp3", relativePath: "new.mp3", size: 300);

        var chain = new FilterChain([new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude)]);
        var result = (await chain.ApplyAsync([oldFile, newFile], comparisonProvider: fs)).ToList();

        Assert.Single(result);
        Assert.Equal("new.mp3", result[0].Name);
    }

    [Fact]
    public async Task FilterChain_MirrorExclude_NestedFiles_ExcludesOnlyMirroredOnes()
    {
        var fs = new MemoryFileSystemProvider();
        // Mirror contains the Curve album but not Algiers
        fs.SeedSimulatedFile("/mirror/Alternative/Curve/song.mp3", size: 1000);

        var curveSong = MakeFile("song.mp3", relativePath: "Alternative/Curve/song.mp3", size: 1000);
        var algiersSong = MakeFile("pain.mp3", relativePath: "Alternative/Algiers/pain.mp3", size: 800);

        var chain = new FilterChain([new MirrorFilter("/mirror", MirrorCompareMode.NameOnly, FilterMode.Exclude)]);
        var result = (await chain.ApplyAsync([curveSong, algiersSong], comparisonProvider: fs)).ToList();

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
            RelativePath = relativePath,
            IsDirectory = false,
            Size = size,
        };

    private static FileSystemNode MakeDirectory(string name, string relativePath) =>
        new()
        {
            Name = name,
            FullPath = "/source/" + relativePath,
            RelativePath = relativePath,
            IsDirectory = true,
        };
}
