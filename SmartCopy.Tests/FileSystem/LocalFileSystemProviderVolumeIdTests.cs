using SmartCopy.Core.FileSystem;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.FileSystem;

/// <summary>
/// <see cref="LocalFileSystemProvider.VolumeId"/> is the identity two providers compare to decide whether
/// they sit on one volume. Getting it wrong is silent: moves stop taking the rename fast path, the
/// same-volume HDD buffer routing never applies, and the volume-keyed caches
/// (<see cref="SmartCopy.Core.FileSystem.Hardware.DriveClassificationRegistry"/>, <c>FreeSpaceCache</c>)
/// degrade to one entry per folder — on macOS that means re-running <c>diskutil</c> per source folder.
/// </summary>
public sealed class LocalFileSystemProviderVolumeIdTests
{
    /// <summary>A representative Unix mount table, in the shape <c>DriveInfo.GetDrives()</c> reports.</summary>
    private static readonly string[] MountPoints =
    [
        "/",
        "/dev",
        "/System/Volumes/Data",
        "/Volumes/PortableSSD",
    ];

    /// <summary>
    /// The regression this fixes: <c>new DriveInfo(path).Name</c> echoes the path back on Unix, so two
    /// folders on one disk used to report different volume IDs and never compared equal.
    /// </summary>
    [Fact]
    public void Unix_TwoFoldersOnOneVolume_ShareAVolumeId()
    {
        if (OperatingSystem.IsWindows()) return;

        var source = new LocalFileSystemProvider("/Volumes/PortableSSD/Source", readMountPoints: () => MountPoints);
        var target = new LocalFileSystemProvider("/Volumes/PortableSSD/Archive/2026", readMountPoints: () => MountPoints);

        Assert.Equal("/Volumes/PortableSSD", source.VolumeId);
        Assert.Equal(source.VolumeId, target.VolumeId);
    }

    [Fact]
    public void Unix_FoldersOnDifferentVolumes_DoNotShareAVolumeId()
    {
        if (OperatingSystem.IsWindows()) return;

        var internalDisk = new LocalFileSystemProvider("/Users/alice/Pictures", readMountPoints: () => MountPoints);
        var externalDisk = new LocalFileSystemProvider("/Volumes/PortableSSD/Pictures", readMountPoints: () => MountPoints);

        Assert.Equal("/", internalDisk.VolumeId);
        Assert.Equal("/Volumes/PortableSSD", externalDisk.VolumeId);
        Assert.NotEqual(internalDisk.VolumeId, externalDisk.VolumeId);
    }

    [Fact]
    public void Unix_VolumeIdIsTheMountPointNotTheRootPath()
    {
        if (OperatingSystem.IsWindows()) return;

        var provider = new LocalFileSystemProvider("/Users/alice/Pictures", readMountPoints: () => MountPoints);

        Assert.Equal("/", provider.VolumeId);
        Assert.NotEqual(provider.RootPath, provider.VolumeId);
    }

    /// <summary>
    /// With no mount point containing the path the ID falls back to null rather than guessing. Null never
    /// compares equal (see the <c>VolumeId is { } vid</c> guards at the call sites), so an unreadable mount
    /// table costs the rename fast path but cannot produce a wrong same-volume claim.
    /// </summary>
    [Fact]
    public void Unix_NoMatchingMountPoint_YieldsNullRatherThanAGuess()
    {
        if (OperatingSystem.IsWindows()) return;

        var provider = new LocalFileSystemProvider("/Volumes/PortableSSD/Source", readMountPoints: () => []);

        Assert.Null(provider.VolumeId);
    }

    /// <summary>End-to-end against the host's real mount table, with no injection.</summary>
    [Fact]
    public void RealFileSystem_SiblingTempFolders_ShareAVolumeId()
    {
        using var left = new TempDirectory();
        using var right = new TempDirectory();

        var leftProvider = new LocalFileSystemProvider(left.Path);
        var rightProvider = new LocalFileSystemProvider(right.Path);

        Assert.NotNull(leftProvider.VolumeId);
        Assert.Equal(leftProvider.VolumeId, rightProvider.VolumeId);

        // On Unix the pre-fix implementation returned the root path itself, so two temp folders
        // disagreed. On Windows the volume is the drive root, which is also not the folder path.
        Assert.NotEqual(leftProvider.RootPath, leftProvider.VolumeId);
    }

    [Fact]
    public void Windows_VolumeIdIsTheDriveRoot()
    {
        if (!OperatingSystem.IsWindows()) return;

        var provider = new LocalFileSystemProvider(@"C:\Users\alice\Pictures");

        Assert.Equal(@"C:\", provider.VolumeId);
    }
}
