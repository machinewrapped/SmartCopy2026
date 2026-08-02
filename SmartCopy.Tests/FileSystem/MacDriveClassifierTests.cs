using SmartCopy.Core.FileSystem.Hardware;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.FileSystem;

/// <summary>
/// Covers the platform-independent parts of the macOS classifier: reading the mount table, resolving
/// a path to its volume (diskutil rejects paths below the mount point) and parsing
/// <c>diskutil info -plist</c>.
/// </summary>
public class MacDriveClassifierTests
{
    /// <summary>Verbatim <c>mount(8)</c> output, including a mount point containing a space.</summary>
    private const string MountTable = """
        /dev/disk3s1s1 on / (apfs, sealed, local, read-only, journaled)
        devfs on /dev (devfs, local, nobrowse)
        /dev/disk3s5 on /System/Volumes/Data (apfs, local, journaled, nobrowse, protect, root data)
        map auto_home on /System/Volumes/Data/home (autofs, automounted, nobrowse)
        pCloud.fs on /Users/simonbooth/pCloud Drive (pcloudfs, nodev, nosuid, synchronous)
        /dev/disk4s1 on /Volumes/PortableSSD (ntfs, local, nodev, nosuid, read-only, noowners, fskit)
        /dev/disk5s1 on /Volumes/4TB exFAT (exfat, local, nodev, nosuid, noowners, noatime, fskit)
        """;

    private static List<MacDriveClassifier.MountEntry> Mounts() =>
        MacDriveClassifier.ParseMountTable(MountTable);

    [Theory]
    [InlineData("/Volumes/PortableSSD/TestData/MixedDataset", "/Volumes/PortableSSD", "disk4s1")]
    [InlineData("/Volumes/PortableSSD", "/Volumes/PortableSSD", "disk4s1")]
    [InlineData("/Volumes/PortableSSD/", "/Volumes/PortableSSD", "disk4s1")]
    [InlineData("/Volumes/4TB exFAT/Backup", "/Volumes/4TB exFAT", "disk5s1")]
    [InlineData("/System/Volumes/Data/Users/bob", "/System/Volumes/Data", "disk3s5")]
    [InlineData("/Users/bob/Documents", "/", "disk3s1s1")]
    [InlineData("/", "/", "disk3s1s1")]
    public void ResolveMount_ReturnsDeepestContainingVolume(string path, string expectedMountPoint, string expectedDevice)
    {
        var mount = MacDriveClassifier.ResolveMount(path, Mounts());

        Assert.NotNull(mount);
        Assert.Equal(expectedMountPoint, mount!.Value.MountPoint);
        Assert.Equal(expectedDevice, mount.Value.DeviceIdentifier);
    }

    /// <summary>Deepest match wins even when a longer mount point sits inside another.</summary>
    [Fact]
    public void ResolveMount_PrefersNestedMountOverItsParent()
    {
        var mount = MacDriveClassifier.ResolveMount("/System/Volumes/Data/home/bob", Mounts());

        Assert.Equal("/System/Volumes/Data/home", mount!.Value.MountPoint);
    }

    /// <summary>autofs maps and userspace filesystems have no block device for diskutil to describe.</summary>
    [Theory]
    [InlineData("/System/Volumes/Data/home/bob")]
    [InlineData("/Users/simonbooth/pCloud Drive/Photos")]
    public void ResolveMount_ReportsNoDeviceIdentifierForNonBlockMounts(string path)
    {
        var mount = MacDriveClassifier.ResolveMount(path, Mounts());

        Assert.NotNull(mount);
        Assert.Null(mount!.Value.DeviceIdentifier);
    }

    [Fact]
    public void ResolveMount_DoesNotMatchSiblingWithSharedPrefix()
    {
        List<MacDriveClassifier.MountEntry> mounts = [new("/dev/disk9s1", "/Volumes/Data")];

        Assert.Null(MacDriveClassifier.ResolveMount("/Volumes/Data2/Files", mounts));
    }

    [Fact]
    public void ResolveMount_ReturnsNullWhenNoMountContainsPath()
    {
        List<MacDriveClassifier.MountEntry> mounts = [new("/dev/disk4s1", "/Volumes/PortableSSD")];

        Assert.Null(MacDriveClassifier.ResolveMount("/Users/bob", mounts));
    }

    [Fact]
    public void ParseMountTable_ReadsDeviceAndMountPointIncludingSpaces()
    {
        var mounts = Mounts();

        Assert.Equal(7, mounts.Count);
        Assert.Contains(mounts, m => m.Device == "/dev/disk5s1" && m.MountPoint == "/Volumes/4TB exFAT");
        Assert.Contains(mounts, m => m.Device == "pCloud.fs" && m.MountPoint == "/Users/simonbooth/pCloud Drive");
        Assert.Contains(mounts, m => m.Device == "map auto_home" && m.MountPoint == "/System/Volumes/Data/home");
    }

    /// <summary>
    /// A trailing space is legal in a volume name and significant: trimming it would leave the mount
    /// unable to match its own contents, which would then be attributed to the parent volume.
    /// </summary>
    [Fact]
    public void ParseMountTable_PreservesTrailingWhitespaceInMountPoints()
    {
        var mounts = MacDriveClassifier.ParseMountTable("/dev/disk9s1 on /Volumes/Archive  (exfat, local)\r\n");

        Assert.Equal("/Volumes/Archive ", mounts[0].MountPoint);
        Assert.Equal("disk9s1", mounts[0].DeviceIdentifier);

        var mount = MacDriveClassifier.ResolveMount("/Volumes/Archive /Backup", mounts);
        Assert.Equal("/Volumes/Archive ", mount!.Value.MountPoint);
    }

    [Fact]
    public void ParseMountTable_SkipsMalformedLines()
    {
        var mounts = MacDriveClassifier.ParseMountTable("garbage without a separator\n\n/dev/disk1 on / (apfs)");

        Assert.Single(mounts);
        Assert.Equal("/", mounts[0].MountPoint);
    }

    [Fact]
    public void ParseInfo_ReadsSolidStateAndBusProtocol()
    {
        var classification = MacDriveClassifier.ParseInfo(Plist(
            "<key>SolidState</key><true/>",
            "<key>BusProtocol</key><string>PCI-Express</string>")).Classification;

        Assert.Equal(DriveMediaType.SSD, classification.MediaType);
        Assert.Equal(DriveInterfaceType.NVMe, classification.InterfaceType);
    }

    /// <summary>Apple Silicon internal storage reports the "Apple Fabric" bus, not PCI-Express.</summary>
    [Fact]
    public void ParseInfo_MapsAppleFabricToNvme()
    {
        var classification = MacDriveClassifier.ParseInfo(Plist(
            "<key>SolidState</key><true/>",
            "<key>BusProtocol</key><string>Apple Fabric</string>")).Classification;

        Assert.Equal(DriveMediaType.SSD, classification.MediaType);
        Assert.Equal(DriveInterfaceType.NVMe, classification.InterfaceType);
    }

    [Fact]
    public void ParseInfo_ReportsHddWhenSolidStateIsFalse()
    {
        var classification = MacDriveClassifier.ParseInfo(Plist(
            "<key>SolidState</key><false/>",
            "<key>BusProtocol</key><string>SATA</string>")).Classification;

        Assert.Equal(DriveMediaType.HDD, classification.MediaType);
        Assert.Equal(DriveInterfaceType.SATA, classification.InterfaceType);
    }

    /// <summary>USB bridges do not report rotation, so the media type must stay Unknown.</summary>
    [Fact]
    public void ParseInfo_ReportsInterfaceOnlyWhenSolidStateIsAbsent()
    {
        var classification = MacDriveClassifier.ParseInfo(Plist(
            "<key>VolumeName</key><string>PortableSSD</string>",
            "<key>BusProtocol</key><string>USB</string>")).Classification;

        Assert.Equal(DriveMediaType.Unknown, classification.MediaType);
        Assert.Equal(DriveInterfaceType.USB, classification.InterfaceType);
        Assert.Equal("USB", classification.ToString());
    }

    [Fact]
    public void ParseInfo_ReturnsUnknownForDiskUtilErrorDocument()
    {
        var classification = MacDriveClassifier.ParseInfo(Plist(
            "<key>Error</key><true/>",
            "<key>ErrorMessage</key><string>Could not find disk</string>")).Classification;

        Assert.Equal(DriveClassification.Unknown, classification);
    }

    [Fact]
    public void ParseInfo_ReturnsUnknownForMalformedXml()
    {
        Assert.Equal(DriveClassification.Unknown, MacDriveClassifier.ParseInfo("not xml").Classification);
    }

    /// <summary>The volume record points at the whole disk, which is where the device name lives.</summary>
    [Fact]
    public void ParseInfo_ReadsParentWholeDisk()
    {
        var info = MacDriveClassifier.ParseInfo(Plist(
            "<key>BusProtocol</key><string>USB</string>",
            "<key>ParentWholeDisk</key><string>disk4</string>"));

        Assert.Equal("disk4", info.ParentWholeDisk);
    }

    [Fact]
    public void ParseInfo_PrefersIoRegistryEntryNameOverMediaName()
    {
        var info = MacDriveClassifier.ParseInfo(Plist(
            "<key>IORegistryEntryName</key><string>SanDisk Portable SSD Media</string>",
            "<key>MediaName</key><string>Portable SSD</string>"));

        Assert.Equal("SanDisk Portable SSD Media", info.DeviceName);
    }

    /// <summary>A volume record carries both keys but leaves them empty.</summary>
    [Fact]
    public void ParseInfo_TreatsBlankNamesAsAbsent()
    {
        var info = MacDriveClassifier.ParseInfo(Plist(
            "<key>IORegistryEntryName</key><string></string>",
            "<key>MediaName</key><string></string>"));

        Assert.Null(info.DeviceName);
    }

    [UnixFact]
    public async Task RunAsync_ReportsTransientTimeoutWhenToolDoesNotAnswer()
    {
        await Assert.ThrowsAsync<DriveClassificationTimeoutException>(() =>
            MacDriveClassifier.RunAsync(SleepPath, ["30"], TimeSpan.FromMilliseconds(100)));
    }

    [UnixFact]
    public async Task RunAsync_ReturnsNullWhenToolExitsNonZero()
    {
        Assert.Null(await MacDriveClassifier.RunAsync(
            SleepPath, ["not-a-duration"], TimeSpan.FromSeconds(30)));
    }

    [UnixFact]
    public async Task RunAsync_PropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var thrown = await Assert.ThrowsAnyAsync<Exception>(() =>
            MacDriveClassifier.RunAsync(SleepPath, ["30"], TimeSpan.FromSeconds(30), cancellation.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
    }

    private const string SleepPath = "/bin/sleep";

    /// <summary>The DOCTYPE mirrors real diskutil output, which the parser must tolerate.</summary>
    private static string Plist(params string[] entries) =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
        """
        + string.Join('\n', entries)
        + """
        </dict>
        </plist>
        """;
}
