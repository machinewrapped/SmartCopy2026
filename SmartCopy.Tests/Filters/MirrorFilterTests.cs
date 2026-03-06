using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Filters;

public sealed class MirrorFilterTests
{
    private const string SourceRoot = "/source";
    private const string MirrorRoot = "/mirror";
    private const string MirrorProviderPath = "/mem/mirror";

    // -------------------------------------------------------------------------
    // NameOnly mode
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MatchesAsync_NameOnly_ReturnsTrueWhenFileExistsInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile($"{SourceRoot}/song.mp3", size: 500)
            .WithSimulatedFile($"{MirrorRoot}/song.mp3", size: 1000));

        var root = await provider.BuildDirectoryTree(SourceRoot);
        var node = root.FindNodeByPathSegments(["song.mp3"]);
        Assert.NotNull(node);

        var filter = new MirrorFilter(MirrorProviderPath, MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.True(await filter.MatchesAsync(node, TestAppContext.FromProvider(provider)));
    }

    [Fact]
    public async Task MatchesAsync_NameOnly_ReturnsFalseWhenFileNotInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile($"{SourceRoot}/new.mp3", size: 500));

        var root = await provider.BuildDirectoryTree(SourceRoot);
        var node = root.FindNodeByPathSegments(["new.mp3"]);
        Assert.NotNull(node);

        var filter = new MirrorFilter(MirrorProviderPath, MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(node, TestAppContext.FromProvider(provider)));
    }

    [Fact]
    public async Task MatchesAsync_NameOnly_NestedPath_ReturnsTrueWhenExistsInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile($"{SourceRoot}/Alternative/Curve/song.mp3", size: 1000)
            .WithSimulatedFile($"{MirrorRoot}/Alternative/Curve/song.mp3", size: 1000));

        var root = await provider.BuildDirectoryTree(SourceRoot);
        var node = root.FindNodeByPathSegments(["Alternative", "Curve", "song.mp3"]);
        Assert.NotNull(node);

        var filter = new MirrorFilter(MirrorProviderPath, MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.True(await filter.MatchesAsync(node, TestAppContext.FromProvider(provider)));
    }

    [Fact]
    public async Task MatchesAsync_NameOnly_NestedPath_ReturnsFalseWhenNotInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile($"{SourceRoot}/Alternative/Curve/song.mp3", size: 1000)
            // Sibling file exists in mirror but not the exact path
            .WithSimulatedFile($"{MirrorRoot}/Alternative/Curve/other.mp3", size: 1000));

        var root = await provider.BuildDirectoryTree(SourceRoot);
        var node = root.FindNodeByPathSegments(["Alternative", "Curve", "song.mp3"]);
        Assert.NotNull(node);

        var filter = new MirrorFilter(MirrorProviderPath, MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(node, TestAppContext.FromProvider(provider)));
    }

    // -------------------------------------------------------------------------
    // NameAndSize mode
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MatchesAsync_NameAndSize_ReturnsTrueWhenNameAndSizeMatch()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile($"{SourceRoot}/song.mp3", size: 1000)
            .WithSimulatedFile($"{MirrorRoot}/song.mp3", size: 1000));

        var root = await provider.BuildDirectoryTree(SourceRoot);
        var node = root.FindNodeByPathSegments(["song.mp3"]);
        Assert.NotNull(node);

        var filter = new MirrorFilter(MirrorProviderPath, MirrorCompareMode.NameAndSize, FilterMode.Exclude);

        Assert.True(await filter.MatchesAsync(node, TestAppContext.FromProvider(provider)));
    }

    [Fact]
    public async Task MatchesAsync_NameAndSize_ReturnsFalseWhenSizeDiffers()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile($"{SourceRoot}/song.mp3", size: 1000)
            .WithSimulatedFile($"{MirrorRoot}/song.mp3", size: 999)); // different size

        var root = await provider.BuildDirectoryTree(SourceRoot);
        var node = root.FindNodeByPathSegments(["song.mp3"]);
        Assert.NotNull(node);

        var filter = new MirrorFilter(MirrorProviderPath, MirrorCompareMode.NameAndSize, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(node, TestAppContext.FromProvider(provider)));
    }

    [Fact]
    public async Task MatchesAsync_NameAndSize_ReturnsFalseWhenFileNotInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile($"{SourceRoot}/song.mp3", size: 1000));
            // No file seeded in mirror

        var root = await provider.BuildDirectoryTree(SourceRoot);
        var node = root.FindNodeByPathSegments(["song.mp3"]);
        Assert.NotNull(node);

        var filter = new MirrorFilter(MirrorProviderPath, MirrorCompareMode.NameAndSize, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(node, TestAppContext.FromProvider(provider)));
    }

    // -------------------------------------------------------------------------
    // Directory matching — a directory is "mirrored" only if it exists in the
    // mirror AND every direct file inside it also exists (and matches) there.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MatchesAsync_Directory_ReturnsTrueWhenEmptyDirectoryExistsInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory($"{SourceRoot}/Alternative")
            .WithDirectory($"{MirrorRoot}/Alternative"));

        var filter = new MirrorFilter(MirrorProviderPath, MirrorCompareMode.NameOnly, FilterMode.Exclude);

        var root = await provider.BuildDirectoryTree(SourceRoot);
        var dir = root.FindNodeByPathSegments(["Alternative"]);
        Assert.NotNull(dir);

        // No files inside → vacuously all files are mirrored
        Assert.True(await filter.MatchesAsync(dir, TestAppContext.FromProvider(provider)));
    }

    [Fact]
    public async Task MatchesAsync_Directory_ReturnsFalseWhenDirectoryNotInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile($"{SourceRoot}/song.mp3", size: 1000));
        // Nothing seeded under MirrorRoot

        var root = await provider.BuildDirectoryTree(SourceRoot);
        var dir = root;
        Assert.NotNull(dir);

        var filter = new MirrorFilter(MirrorProviderPath, MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(dir, TestAppContext.FromProvider(provider)));
    }

    [Fact]
    public async Task MatchesAsync_Directory_ReturnsTrueWhenAllFilesInMirror()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory($"{SourceRoot}/Alternative")
            .WithSimulatedFile($"{SourceRoot}/Alternative/song.mp3", size: 1000)
            .WithDirectory($"{MirrorRoot}/Alternative")
            .WithSimulatedFile($"{MirrorRoot}/Alternative/song.mp3", size: 1000));

        var root = await provider.BuildDirectoryTree(SourceRoot);
        var dir = root.FindNodeByPathSegments(["Alternative"]);
        Assert.NotNull(dir);

        var filter = new MirrorFilter(MirrorProviderPath, MirrorCompareMode.NameOnly, FilterMode.Exclude);

        Assert.True(await filter.MatchesAsync(dir, TestAppContext.FromProvider(provider)));
    }

    [Fact]
    public async Task MatchesAsync_Directory_ReturnsFalseWhenSomeFilesNotInMirror()
    {   var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory($"{SourceRoot}/Alternative")
            .WithSimulatedFile($"{SourceRoot}/Alternative/old.mp3", size: 1000)
            .WithSimulatedFile($"{SourceRoot}/Alternative/new.mp3", size: 800)
            .WithDirectory($"{MirrorRoot}/Alternative")
            .WithSimulatedFile($"{MirrorRoot}/Alternative/old.mp3", size: 1000));
            // "new.mp3" is NOT seeded in the mirror

        var filter = new MirrorFilter(MirrorProviderPath, MirrorCompareMode.NameOnly, FilterMode.Exclude);

        var root = await provider.BuildDirectoryTree(SourceRoot);
        var dir = root.FindNodeByPathSegments(["Alternative"]);
        Assert.NotNull(dir);

        Assert.False(await filter.MatchesAsync(dir, TestAppContext.FromProvider(provider)));
    }

    [Fact]
    public async Task MatchesAsync_Directory_NameAndSize_ReturnsFalseWhenFileSizeDiffers()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory($"{SourceRoot}/Alternative")
            .WithSimulatedFile($"{SourceRoot}/Alternative/song.mp3", size: 1000)
            .WithDirectory($"{MirrorRoot}/Alternative")
            .WithSimulatedFile($"{MirrorRoot}/Alternative/song.mp3", size: 999)); // different size

        var root = await provider.BuildDirectoryTree(SourceRoot);
        var dir = root.FindNodeByPathSegments(["Alternative"]);
        Assert.NotNull(dir);

        var filter = new MirrorFilter(MirrorProviderPath, MirrorCompareMode.NameAndSize, FilterMode.Exclude);

        Assert.False(await filter.MatchesAsync(dir, TestAppContext.FromProvider(provider)));
    }

    // -------------------------------------------------------------------------
    // FilterChain integration
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FilterChain_MirrorExclude_ExcludesFilesAlreadyInMirror()
    {
        var fs = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory(SourceRoot)
            .WithSimulatedFile($"{SourceRoot}/old.mp3", size: 500)
            .WithSimulatedFile($"{SourceRoot}/new.mp3", size: 300)
            .WithDirectory(MirrorRoot)
            .WithSimulatedFile($"{MirrorRoot}/old.mp3", size: 500));

        var root = await fs.BuildDirectoryTree(SourceRoot);
        var oldFile = root.FindNodeByPathSegments(["old.mp3"]);
        var newFile = root.FindNodeByPathSegments(["new.mp3"]);
        Assert.NotNull(oldFile);
        Assert.NotNull(newFile);

        var chain = new FilterChain([new MirrorFilter(MirrorProviderPath, MirrorCompareMode.NameOnly, FilterMode.Exclude)]);
        var result = (await chain.ApplyAsync([oldFile, newFile], TestAppContext.FromProvider(fs))).ToList();

        Assert.Single(result);
        Assert.Equal("new.mp3", result[0].Name);
    }

    [Fact]
    public async Task FilterChain_MirrorExclude_NestedFiles_ExcludesOnlyMirroredOnes()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory($"{SourceRoot}/Alternative")
            .WithSimulatedFile($"{SourceRoot}/Alternative/Curve/song.mp3", size: 1000)
            .WithSimulatedFile($"{SourceRoot}/Alternative/Algiers/pain.mp3", size: 800)
            .WithDirectory($"{MirrorRoot}/Alternative")
            .WithSimulatedFile($"{MirrorRoot}/Alternative/Curve/song.mp3", size: 1000));
            // "Algiers/pain.mp3" is NOT seeded in the mirror

        // Mirror contains the Curve album but not Algiers
        var root = await provider.BuildDirectoryTree(SourceRoot);

        var curveSong = root.FindNodeByPathSegments(["Alternative", "Curve", "song.mp3"]);
        var algiersSong = root.FindNodeByPathSegments(["Alternative", "Algiers", "pain.mp3"]);
        Assert.NotNull(curveSong);
        Assert.NotNull(algiersSong);

        var chain = new FilterChain([new MirrorFilter(MirrorProviderPath, MirrorCompareMode.NameOnly, FilterMode.Exclude)]);
        var result = (await chain.ApplyAsync([curveSong, algiersSong], TestAppContext.FromProvider(provider))).ToList();

        Assert.Single(result);
        Assert.Equal("pain.mp3", result[0].Name);
    }

    [Fact]
    public async Task MatchesAsync_UsesLocalProviderWhenComparisonPathIsFullyQualified()
    {
        using var temp = new TempDirectory();
        var mirrorRoot = Path.Combine(temp.Path, "mirror");
        Directory.CreateDirectory(mirrorRoot);
        File.WriteAllBytes(Path.Combine(mirrorRoot, "song.mp3"), [1, 2, 3]);

        var source = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory(SourceRoot)
            .WithSimulatedFile($"{SourceRoot}/song.mp3", size: 3));

        var root = await source.BuildDirectoryTree(SourceRoot);
        var song = root.FindNodeByPathSegments(["song.mp3"]);
        Assert.NotNull(song);

        var filter = new MirrorFilter(mirrorRoot, MirrorCompareMode.NameOnly, FilterMode.Exclude);
        Assert.True(await filter.MatchesAsync(song, new TestAppContext()));
    }
}
