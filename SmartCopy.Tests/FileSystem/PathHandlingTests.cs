using SmartCopy.Core.FileSystem;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.FileSystem;

/// <summary>
/// Verifies path normalization, splitting, joining, and GetRelativePath for both providers.
/// Both providers accept forward-slash and backslash inputs; each normalizes to its own convention.
/// </summary>
public sealed class PathHandlingTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly LocalFileSystemProvider _local;
    private readonly MemoryFileSystemProvider _memory = new();

    public PathHandlingTests()
    {
        _local = new LocalFileSystemProvider(_temp.Path);
    }

    public void Dispose() => _temp.Dispose();

    // -------------------------------------------------------------------------
    // MemoryFileSystemProvider — SplitPath
    // -------------------------------------------------------------------------

    [Fact]
    public void Memory_SplitPath_ForwardSlash()
    {
        Assert.Equal(["music", "rock", "beatles"], _memory.SplitPath("/music/rock/beatles"));
    }

    [Fact]
    public void Memory_SplitPath_BackslashInput()
    {
        Assert.Equal(["music", "rock", "beatles"], _memory.SplitPath(@"music\rock\beatles"));
    }

    [Fact]
    public void Memory_SplitPath_TrailingSlash()
    {
        Assert.Equal(["music", "rock"], _memory.SplitPath("/music/rock/"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    public void Memory_SplitPath_EmptyOrRoot_ReturnsEmptyArray(string path)
    {
        Assert.Empty(_memory.SplitPath(path));
    }

    // -------------------------------------------------------------------------
    // MemoryFileSystemProvider — JoinPath and GetRelativePath
    // -------------------------------------------------------------------------

    [Fact]
    public void Memory_JoinPath_ProducesCanonicalPath()
    {
        var result = _memory.JoinPath("/music", ["rock", "song.mp3"]);
        Assert.Equal("/music/rock/song.mp3", result);
    }

    [Fact]
    public void Memory_GetRelativePath_StripsBase()
    {
        var relative = _memory.GetRelativePath("/music", "/music/rock/song.mp3");
        Assert.Equal("rock/song.mp3", relative);
    }

    [Fact]
    public void Memory_GetRelativePath_EqualPaths_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _memory.GetRelativePath("/music", "/music"));
    }

    // -------------------------------------------------------------------------
    // LocalFileSystemProvider — SplitPath
    // -------------------------------------------------------------------------

    [Fact]
    public void Local_SplitPath_BackslashRelativePath()
    {
        Assert.Equal(["albums", "track1.txt"], _local.SplitPath(@"albums\track1.txt"));
    }

    [Fact]
    public void Local_SplitPath_ForwardSlashRelativePath()
    {
        Assert.Equal(["albums", "track1.txt"], _local.SplitPath("albums/track1.txt"));
    }

    [Fact]
    public void Local_SplitPath_UncStyleInput()
    {
        // UNC-style: \\server\share\file.txt → backslashes normalised → server/share/file.txt
        Assert.Equal(["server", "share", "file.txt"], _local.SplitPath(@"\\server\share\file.txt"));
    }

    [Theory]
    [InlineData("")]
    public void Local_SplitPath_EmptyInput_ReturnsEmptyArray(string path)
    {
        Assert.Empty(_local.SplitPath(path));
    }

    // -------------------------------------------------------------------------
    // LocalFileSystemProvider — JoinPath and GetRelativePath
    // -------------------------------------------------------------------------

    [Fact]
    public void Local_JoinPath_ProducesValidAbsolutePath()
    {
        var result = _local.JoinPath(_local.RootPath, ["sub", "file.txt"]);
        Assert.True(Path.IsPathRooted(result));
        Assert.EndsWith("file.txt", result);
        Assert.Contains("sub", result);
    }

    [Fact]
    public void Local_GetRelativePath_RoundTrip()
    {
        var segments = new[] { "subA", "file.txt" };
        var joined = _local.JoinPath(_local.RootPath, segments);
        var relative = _local.GetRelativePath(_local.RootPath, joined);
        var splitBack = _local.SplitPath(relative);

        Assert.Equal(segments, splitBack);
    }

    [Fact]
    public void Local_GetRelativePath_EqualPaths_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _local.GetRelativePath(_local.RootPath, _local.RootPath));
    }
}
