using SmartCopy.Core.FileSystem;

namespace SmartCopy.Tests.FileSystem;

public sealed class PathHelperTests
{
    [Fact]
    public void NormalizeDistinctUserPaths_DeduplicatesMemoryPaths()
    {
        var normalized = PathHelper.NormalizeDistinctUserPaths(
        [
            "/mem/Music/",
            "/mem/Music",
            "/mem//Music/",
        ]);

        Assert.Single(normalized);
        Assert.Equal("/mem/Music", normalized[0]);
    }

    [Fact]
    public void RemoveTrailingSeparator_PreservesWindowsDriveRoot()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Equal(@"C:\", PathHelper.RemoveTrailingSeparator(@"C:\"));
    }

    [Fact]
    public void RemoveTrailingSeparator_PreservesWindowsUncRoot()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var input = @"\\server\share\";
        var expectedRoot = Path.GetPathRoot(input);

        Assert.Equal(expectedRoot, PathHelper.RemoveTrailingSeparator(input));
    }

    [Fact]
    public void AreEquivalentUserPaths_RecognizesWindowsLocalVariants()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.True(PathHelper.AreEquivalentUserPaths(@"C:\Music\", @"c:/Music"));
    }

    [Fact]
    public void AreEquivalentUserPaths_RecognizesWindowsUncVariants()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.True(PathHelper.AreEquivalentUserPaths(@"\\SERVER\Share\Music\", @"\\server\share\music"));
    }
}
