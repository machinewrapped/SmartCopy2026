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

    [Fact]
    public void GetDeviceName_PrefersConnectedMetadataAndCreatesDistinctFallbacks()
    {
        Assert.Equal(
            "motorola edge 60 fusion",
            MtpFileSystemProvider.GetDeviceName("motorola edge 60 fusion", "Motorola", "device-a"));
        Assert.Equal(
            "Motorola",
            MtpFileSystemProvider.GetDeviceName("", "Motorola", "device-a"));

        var firstFallback = MtpFileSystemProvider.GetDeviceName(null, null, "device-a");
        var secondFallback = MtpFileSystemProvider.GetDeviceName(null, null, "device-b");

        Assert.StartsWith("Connected portable device ", firstFallback);
        Assert.NotEqual(firstFallback, secondFallback);
    }
}
