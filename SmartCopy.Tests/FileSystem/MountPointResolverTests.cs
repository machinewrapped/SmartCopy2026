using SmartCopy.Core.FileSystem;

namespace SmartCopy.Tests.FileSystem;

/// <summary>
/// Pure string-matching tests for <see cref="MountPointResolver"/>. These use injected mount tables
/// rather than the host's, so they are hermetic and run on every platform.
/// </summary>
public sealed class MountPointResolverTests
{
    /// <summary>A representative macOS mount table, as reported by <c>DriveInfo.GetDrives()</c>.</summary>
    private static readonly string[] MacMountPoints =
    [
        "/",
        "/dev",
        "/System/Volumes/VM",
        "/System/Volumes/Data",
        "/Users/alice/pCloud Drive",
        "/Volumes/PortableSSD",
        "/Volumes/4TB exFAT",
    ];

    [Fact]
    public void Resolve_PathOnRootVolume_ReturnsRoot()
    {
        Assert.Equal("/", MountPointResolver.Resolve("/Users/alice/Pictures", MacMountPoints));
    }

    [Fact]
    public void Resolve_PathOnMountedDisk_ReturnsThatMountNotRoot()
    {
        Assert.Equal(
            "/Volumes/PortableSSD",
            MountPointResolver.Resolve("/Volumes/PortableSSD/TestData/MixedDataset", MacMountPoints));
    }

    /// <summary>
    /// The bug this whole helper exists for: two different folders on one volume must produce one
    /// volume ID, so same-volume moves and the volume-keyed caches actually match.
    /// </summary>
    [Fact]
    public void Resolve_TwoFoldersOnOneVolume_AgreeOnVolume()
    {
        var left = MountPointResolver.Resolve("/Volumes/PortableSSD/Source", MacMountPoints);
        var right = MountPointResolver.Resolve("/Volumes/PortableSSD/Archive/2026", MacMountPoints);

        Assert.Equal(left, right);
        Assert.Equal("/Volumes/PortableSSD", left);
    }

    /// <summary>A FUSE mount inside a home directory must beat the root mount that also contains it.</summary>
    [Fact]
    public void Resolve_NestedMountInsideHomeDirectory_WinsOverRoot()
    {
        Assert.Equal(
            "/Users/alice/pCloud Drive",
            MountPointResolver.Resolve("/Users/alice/pCloud Drive/Photos", MacMountPoints));
    }

    [Fact]
    public void Resolve_DeepestMountWins_RegardlessOfEnumerationOrder()
    {
        string[] shallowFirst = ["/", "/mnt", "/mnt/data", "/mnt/data/projects"];
        string[] deepFirst = ["/mnt/data/projects", "/mnt/data", "/mnt", "/"];

        Assert.Equal("/mnt/data/projects", MountPointResolver.Resolve("/mnt/data/projects/x", shallowFirst));
        Assert.Equal("/mnt/data/projects", MountPointResolver.Resolve("/mnt/data/projects/x", deepFirst));
    }

    /// <summary>Prefix matching must be anchored to a segment boundary.</summary>
    [Fact]
    public void Resolve_SiblingSharingANamePrefix_DoesNotClaimThePath()
    {
        string[] mountPoints = ["/", "/Volumes/Data"];

        Assert.Equal("/", MountPointResolver.Resolve("/Volumes/DataBackup/photos", mountPoints));
    }

    [Fact]
    public void Resolve_PathIsTheMountPointItself_ReturnsIt()
    {
        Assert.Equal("/Volumes/PortableSSD", MountPointResolver.Resolve("/Volumes/PortableSSD", MacMountPoints));
    }

    [Fact]
    public void Resolve_TrailingAndDuplicateSeparators_AreNormalized()
    {
        string[] mountPoints = ["/", "/Volumes/PortableSSD/"];

        Assert.Equal(
            "/Volumes/PortableSSD",
            MountPointResolver.Resolve("/Volumes//PortableSSD/Source/", mountPoints));
    }

    [Fact]
    public void Resolve_NoMountPointContainsThePath_ReturnsNull()
    {
        Assert.Null(MountPointResolver.Resolve("/Volumes/Elsewhere/file", ["/Volumes/PortableSSD"]));
    }

    [Fact]
    public void Resolve_EmptyMountTable_ReturnsNull()
    {
        Assert.Null(MountPointResolver.Resolve("/Users/alice", []));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankPath_ReturnsNull(string path)
    {
        Assert.Null(MountPointResolver.Resolve(path, MacMountPoints));
    }

    [Fact]
    public void Resolve_BlankMountPointEntries_AreIgnored()
    {
        string[] mountPoints = ["", "   ", "/"];

        Assert.Equal("/", MountPointResolver.Resolve("/Users/alice", mountPoints));
    }
}
