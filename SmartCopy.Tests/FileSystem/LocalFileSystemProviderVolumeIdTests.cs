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

    /// <summary>
    /// Providers are held in a process-wide registry, so one created while a path was unmounted outlives
    /// that state. A device mounted underneath it afterwards must be picked up: a stale ID would keep
    /// naming the parent volume and could claim same-volume against it, skipping the free-space warning.
    /// </summary>
    [Fact]
    public void Unix_DeviceMountedAfterProviderCreation_IsPickedUp()
    {
        if (OperatingSystem.IsWindows()) return;

        var mounted = false;
        var provider = new LocalFileSystemProvider(
            "/mnt/archive/2026",
            readMountPoints: () => mounted ? ["/", "/mnt/archive"] : ["/"]);

        Assert.Equal("/", provider.VolumeId);

        mounted = true;

        Assert.Equal("/mnt/archive", provider.VolumeId);
    }

    /// <summary>And the reverse, so an unmounted device stops being reported as its own volume.</summary>
    [Fact]
    public void Unix_DeviceUnmountedAfterProviderCreation_IsPickedUp()
    {
        if (OperatingSystem.IsWindows()) return;

        var mounted = true;
        var provider = new LocalFileSystemProvider(
            "/mnt/archive/2026",
            readMountPoints: () => mounted ? ["/", "/mnt/archive"] : ["/"]);

        Assert.Equal("/mnt/archive", provider.VolumeId);

        mounted = false;

        Assert.Equal("/", provider.VolumeId);
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
        // disagreed. On Windows the volume is the volume GUID path, which is also not the folder path.
        Assert.NotEqual(leftProvider.RootPath, leftProvider.VolumeId);
    }

    /// <summary>
    /// The drive root is not the volume: a junction or mount point under it can lead elsewhere. The
    /// volume GUID path is the same however the volume is reached.
    /// </summary>
    [Fact]
    public void Windows_VolumeIdIsTheVolumeGuidPath()
    {
        if (!OperatingSystem.IsWindows()) return;

        using var temp = new TempDirectory();
        var provider = new LocalFileSystemProvider(temp.Path);

        Assert.StartsWith(@"\\?\Volume{", provider.VolumeId);
        Assert.EndsWith(@"}\", provider.VolumeId);
    }

    /// <summary>
    /// A destination usually does not exist until the copy creates it, so both the volume and its free
    /// space must come from the nearest existing ancestor rather than failing.
    /// </summary>
    [Fact]
    public async Task MissingDestination_ResolvesThroughItsDeepestExistingAncestor()
    {
        using var temp = new TempDirectory();
        var existing = new LocalFileSystemProvider(temp.Path);
        var missing = new LocalFileSystemProvider(Path.Combine(temp.Path, "not", "created", "yet"));

        Assert.NotNull(missing.VolumeId);
        Assert.Equal(existing.VolumeId, missing.VolumeId);
        Assert.True(await missing.GetAvailableFreeSpaceAsync(CancellationToken.None) > 0);
    }

    /// <summary>
    /// The UI refreshes free space directly, and on a slow or unreachable share the OS lookup blocks until
    /// the network times out — so the query must run off the caller's thread and honour cancellation.
    /// </summary>
    [Fact]
    public async Task FreeSpaceQuery_HonoursCancellation()
    {
        using var temp = new TempDirectory();
        var provider = new LocalFileSystemProvider(temp.Path);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.GetAvailableFreeSpaceAsync(new CancellationToken(canceled: true)));
    }

    [Theory]
    [InlineData(@"\\?\Volume{6bce7ae7-56ba-46da-b01b-4d08bd04e22d}\Users\alice", @"\\?\Volume{6bce7ae7-56ba-46da-b01b-4d08bd04e22d}\")]
    [InlineData(@"\\?\Volume{6bce7ae7-56ba-46da-b01b-4d08bd04e22d}\", @"\\?\Volume{6bce7ae7-56ba-46da-b01b-4d08bd04e22d}\")]
    [InlineData(@"\\?\UNC\server\share\folder", null)]
    public void Windows_VolumeIdFromGuidPath(string finalPath, string? expected)
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.Equal(expected, WindowsVolumeResolver.VolumeIdFromGuidPath(finalPath));
    }

    /// <summary>
    /// The drive classifier opens a local volume by this path, and must classify a remote volume as
    /// network rather than fall back to the local drive letter the path was written under.
    /// </summary>
    [Theory]
    [InlineData(@"\\?\Volume{6bce7ae7-56ba-46da-b01b-4d08bd04e22d}\", @"\\?\Volume{6bce7ae7-56ba-46da-b01b-4d08bd04e22d}")]
    [InlineData(@"\\MINIPC\Media|FE07D85A", null)]
    public void Windows_LocalVolumeDevicePath(string volumeId, string? expected)
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.Equal(expected, WindowsVolumeResolver.LocalVolumeDevicePath(volumeId));
    }

    /// <summary>
    /// A mapped drive and its UNC path are the same share, so a remote ID is keyed on the share the
    /// handle resolved to, never the drive letter it was reached through.
    /// </summary>
    [Theory]
    [InlineData(@"\\?\UNC\MINIPC\Media\New Movies", @"\\MINIPC\Media")]
    [InlineData(@"\\?\UNC\MINIPC\Media", @"\\MINIPC\Media")]
    [InlineData(@"\\?\C:\Media\Disk2", @"C:\")]
    public void Windows_RootFromFinalPath(string finalPath, string expected)
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.Equal(expected, WindowsVolumeResolver.RootFromFinalPath(finalPath));
    }
}
