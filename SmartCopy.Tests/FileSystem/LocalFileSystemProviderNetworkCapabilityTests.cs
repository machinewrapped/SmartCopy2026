using SmartCopy.Core.FileSystem;

namespace SmartCopy.Tests.FileSystem;

public sealed class LocalFileSystemProviderNetworkCapabilityTests
{
    [Fact]
    public void Local_LinuxCifsMount_DegradesCapabilities()
    {
        if (!OperatingSystem.IsLinux())
            return;

        const string mountInfo = """
            24 23 8:1 / / rw,relatime - ext4 /dev/sda1 rw
            31 24 0:29 / /mnt/share rw,relatime - cifs //server/share rw,vers=3.0
            """;

        var provider = new LocalFileSystemProvider("/mnt/share/projects", () => mountInfo);
        var caps = provider.Capabilities;

        Assert.False(caps.CanWatch);
        Assert.False(caps.CanTrash);
        Assert.False(caps.CanAtomicMove);
        Assert.Null(provider.VolumeId);
    }

    [Fact]
    public void Local_LinuxLocalMount_KeepsCapabilities()
    {
        if (!OperatingSystem.IsLinux())
            return;

        const string mountInfo = """
            24 23 8:1 / / rw,relatime - ext4 /dev/sda1 rw
            31 24 8:1 /data /mnt/share rw,relatime - ext4 /dev/sda1 rw
            """;

        var provider = new LocalFileSystemProvider("/mnt/share/projects", () => mountInfo);
        var caps = provider.Capabilities;

        Assert.True(caps.CanWatch);
        Assert.True(caps.CanTrash);
        Assert.True(caps.CanAtomicMove);
        Assert.NotNull(provider.VolumeId);
    }

    [Fact]
    public void Local_LinuxDetection_UsesLongestMountPoint()
    {
        if (!OperatingSystem.IsLinux())
            return;

        const string mountInfo = """
            24 23 8:1 / / rw,relatime - ext4 /dev/sda1 rw
            25 24 8:1 /mnt /mnt rw,relatime - ext4 /dev/sda1 rw
            31 25 0:29 / /mnt/share rw,relatime - nfs4 server:/exports/share rw
            """;

        var provider = new LocalFileSystemProvider("/mnt/share/projects", () => mountInfo);
        var caps = provider.Capabilities;

        Assert.False(caps.CanWatch);
        Assert.False(caps.CanTrash);
        Assert.False(caps.CanAtomicMove);
        Assert.Null(provider.VolumeId);
    }

    [Fact]
    public void Local_LinuxMountInfoReadFailure_FallsBackToLocalCapabilities()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var provider = new LocalFileSystemProvider("/tmp", () => throw new IOException("mountinfo not available"));
        var caps = provider.Capabilities;

        Assert.True(caps.CanWatch);
        Assert.True(caps.CanTrash);
        Assert.True(caps.CanAtomicMove);
        Assert.NotNull(provider.VolumeId);
    }
}
