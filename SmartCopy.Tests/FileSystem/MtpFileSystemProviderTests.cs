using SmartCopy.Core.FileSystem;

namespace SmartCopy.Tests.FileSystem;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class MtpFileSystemProviderTests
{
    [Fact]
    public void SortChildren_PutsDirectoriesFirstThenUsesWindowsStyleNameOrder()
    {
        var sorted = MtpFileSystemProvider.SortChildren(
        [
            new FileSystemNode { Name = "zebra.mp3", IsDirectory = false },
            new FileSystemNode { Name = "World & New Age", IsDirectory = true },
            new FileSystemNode { Name = "__Just Ripped", IsDirectory = true },
            new FileSystemNode { Name = "Alternative", IsDirectory = true },
            new FileSystemNode { Name = "alpha.mp3", IsDirectory = false },
        ]);

        Assert.Equal(
            ["__Just Ripped", "Alternative", "World & New Age", "alpha.mp3", "zebra.mp3"],
            sorted.Select(node => node.Name));
    }
}
