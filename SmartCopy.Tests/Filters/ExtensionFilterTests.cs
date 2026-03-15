using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Filters;

public sealed class ExtensionFilterTests
{
    // -------------------------------------------------------------------------
    // ParseExtensions — input parsing
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("*.mp3",             new[] { "mp3" })]
    [InlineData("*.MP3",             new[] { "mp3" })]    // case-folded
    [InlineData(".mp3",              new[] { "mp3" })]    // leading dot
    [InlineData("mp3",               new[] { "mp3" })]    // bare name
    [InlineData("*mp3",              new[] { "mp3" })]    // glob without dot
    public void ParseExtensions_SingleToken_Normalizes(string input, string[] expected)
    {
        var result = ExtensionFilter.ParseExtensions(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseExtensions_CompositeSemicolonList_SplitsAndNormalizes()
    {
        var result = ExtensionFilter.ParseExtensions("*.mp3;*.mp4;*.mp5");
        Assert.Equal(["mp3", "mp4", "mp5"], result);
    }

    [Fact]
    public void ParseExtensions_MixedFormats_HandlesAll()
    {
        // Mix of glob, dot-prefixed and bare names
        var result = ExtensionFilter.ParseExtensions("*.mp3; .flac; aac; *.OGG");
        Assert.Equal(["mp3", "flac", "aac", "ogg"], result);
    }

    [Fact]
    public void ParseExtensions_EmptySegmentsAndWhitespace_Ignored()
    {
        var result = ExtensionFilter.ParseExtensions("*.mp3;;  ;*.flac");
        Assert.Equal(["mp3", "flac"], result);
    }

    [Fact]
    public void ParseExtensions_EmptyString_ReturnsEmpty()
    {
        var result = ExtensionFilter.ParseExtensions(string.Empty);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseExtensions_OnlyWildcard_Ignored()
    {
        // A bare "*" with no extension portion should produce nothing
        var result = ExtensionFilter.ParseExtensions("*");
        Assert.Empty(result);
    }

    // -------------------------------------------------------------------------
    // IsValidExtension
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("mp3",    true)]
    [InlineData("FLAC",   true)]
    [InlineData("x.y",    false)] // embedded dot
    [InlineData("",       false)] // empty
    [InlineData(" ",      false)] // whitespace
    public void IsValidExtension_ValidatesCorrectly(string normalized, bool expected)
    {
        var result = ExtensionFilter.IsValidExtension(normalized);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("x*y")]
    [InlineData("x?y")]
    [InlineData("x:y")]
    public void IsValidExtension_WildcardsAndSpecialChars_AreInvalidOnlyOnWindows(string normalized)
    {
        var result = ExtensionFilter.IsValidExtension(normalized);
        
        // On Windows, these are invalid filename chars. 
        // On Linux/macOS, they are technically allowed, so ExtensionFilter accepts them.
        if (OperatingSystem.IsWindows())
        {
            Assert.False(result);
        }
        else
        {
            Assert.True(result);
        }
    }

    // -------------------------------------------------------------------------
    // Constructor — still accepts plain extension strings unchanged
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_WithPlainExtensions_NormalizesCorrectly()
    {
        var filter = new ExtensionFilter(["mp3", ".FLAC", " aac "], FilterMode.Only);
        Assert.Equal(["mp3", "flac", "aac"], filter.Extensions);
    }

    // -------------------------------------------------------------------------
    // MatchesAsync — glob-style round-trip through ParseExtensions
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MatchesAsync_GlobInput_MatchesExpectedFiles()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/src/song.mp3",  size: 100)
            .WithSimulatedFile("/src/photo.jpg", size: 200)
            .WithSimulatedFile("/src/clip.mp4",  size: 300));

        var root = await provider.BuildDirectoryTree("/src");

        var extensions = ExtensionFilter.ParseExtensions("*.mp3;*.mp4");
        var filter = new ExtensionFilter(extensions, FilterMode.Only);

        var mp3  = root.FindNodeByPathSegments(["song.mp3"]);
        var jpg  = root.FindNodeByPathSegments(["photo.jpg"]);
        var mp4  = root.FindNodeByPathSegments(["clip.mp4"]);
        var ctx  = TestAppContext.FromProvider(provider);

        Assert.NotNull(mp3);
        Assert.NotNull(jpg);
        Assert.NotNull(mp4);

        Assert.True(await filter.MatchesAsync(mp3, ctx));
        Assert.True(await filter.MatchesAsync(mp4, ctx));
        Assert.False(await filter.MatchesAsync(jpg, ctx));
    }
}
