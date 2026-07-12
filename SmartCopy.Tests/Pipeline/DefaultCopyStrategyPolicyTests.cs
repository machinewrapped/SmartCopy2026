using SmartCopy.Core.FileSystem;
using SmartCopy.Core.FileSystem.Hardware;
using SmartCopy.Core.Pipeline.Strategy;

namespace SmartCopy.Tests.Pipeline;

/// <summary>
/// Tests for <see cref="DefaultCopyStrategyPolicy"/>: buffer selection from the drive pair when
/// destination routing is enabled, strategy-type selection, and no-behaviour-change when disabled.
/// </summary>
public sealed class DefaultCopyStrategyPolicyTests
{
    private const int BaseBuffer = 384 * 1024;
    private const int SsdBuffer = 1536 * 1024;
    private const int UsbBuffer = 1792 * 1024;
    private const int HddBuffer = 640 * 1024;
    private const int SameVolumeHddBuffer = 256 * 1024;
    private const int UnknownBuffer = 320 * 1024;

    private static ICopyStrategy Resolve(
        OperationalSettings settings,
        DriveClassification source,
        DriveClassification target,
        bool sameVolume = false) =>
        DefaultCopyStrategyPolicy.Instance.Resolve(new CopyStrategyInputs(
            settings, source, target, ProviderCapabilities.Full, ProviderCapabilities.Full, sameVolume));

    public static TheoryData<DriveMediaType, DriveInterfaceType, DriveMediaType, DriveInterfaceType, bool, int>
        RoutingCases => new()
        {
            { DriveMediaType.SSD, DriveInterfaceType.NVMe, DriveMediaType.SSD, DriveInterfaceType.NVMe, false, SsdBuffer },
            { DriveMediaType.SSD, DriveInterfaceType.NVMe, DriveMediaType.SSD, DriveInterfaceType.USB, false, UsbBuffer },
            { DriveMediaType.SSD, DriveInterfaceType.NVMe, DriveMediaType.HDD, DriveInterfaceType.SATA, false, HddBuffer },
            { DriveMediaType.HDD, DriveInterfaceType.SATA, DriveMediaType.SSD, DriveInterfaceType.NVMe, false, HddBuffer },
            { DriveMediaType.HDD, DriveInterfaceType.SATA, DriveMediaType.HDD, DriveInterfaceType.SATA, true, SameVolumeHddBuffer },
            { DriveMediaType.Unknown, DriveInterfaceType.Unknown, DriveMediaType.Unknown, DriveInterfaceType.Unknown, false, UnknownBuffer },
        };

    [Theory]
    [MemberData(nameof(RoutingCases))]
    public void RoutingEnabled_SelectsBufferFromDrivePair(
        DriveMediaType srcMedia, DriveInterfaceType srcIface,
        DriveMediaType dstMedia, DriveInterfaceType dstIface,
        bool sameVolume, int expectedBuffer)
    {
        var strategy = Resolve(
            RoutedSettings(),
            new DriveClassification(srcMedia, srcIface),
            new DriveClassification(dstMedia, dstIface),
            sameVolume);

        Assert.Equal(expectedBuffer, strategy.Settings.CopyBufferSizeBytes);
    }

    [Theory]
    [InlineData(0, typeof(StreamingCopyStrategy))]
    [InlineData(1024 * 1024, typeof(BatchedCopyStrategy))]
    public void SelectsStrategyType_FromBatchBuffer(long batchBufferBytes, Type expectedType)
    {
        var strategy = Resolve(
            new OperationalSettings { BatchBufferBytes = batchBufferBytes },
            DriveClassification.Unknown, DriveClassification.Unknown);

        Assert.IsType(expectedType, strategy);
    }

    [Fact]
    public void RoutingDisabled_KeepsBaseBuffer()
    {
        var baseSettings = new OperationalSettings(); // routing off
        var strategy = Resolve(baseSettings,
            new DriveClassification(DriveMediaType.SSD, DriveInterfaceType.NVMe),
            new DriveClassification(DriveMediaType.HDD, DriveInterfaceType.SATA));

        Assert.Equal(baseSettings.CopyBufferSizeBytes, strategy.Settings.CopyBufferSizeBytes);
    }

    [Fact]
    public void RoutingEnabled_SameVolumeHdd_UsesCanonical256KiBBuffer()
    {
        var strategy = Resolve(
            RoutedSettings(),
            new DriveClassification(DriveMediaType.HDD, DriveInterfaceType.SATA),
            new DriveClassification(DriveMediaType.HDD, DriveInterfaceType.SATA),
            sameVolume: true);

        Assert.Equal(256 * 1024, strategy.Settings.CopyBufferSizeBytes);
    }

    [Fact]
    public void RoutingEnabled_HddSource_DisablesBatchOrderBySize()
    {
        var strategy = Resolve(
            new OperationalSettings
            {
                DestinationRoutingEnabled = true,
                BatchOrderByFileSize = true,
            },
            new DriveClassification(DriveMediaType.HDD, DriveInterfaceType.SATA),
            new DriveClassification(DriveMediaType.HDD, DriveInterfaceType.SATA),
            sameVolume: true);

        Assert.False(strategy.Settings.BatchOrderByFileSize);
    }

    [Fact]
    public void RoutingEnabled_HddSourceToSsd_DisablesBatchOrderBySize()
    {
        var strategy = Resolve(
            new OperationalSettings
            {
                DestinationRoutingEnabled = true,
                BatchOrderByFileSize = true,
            },
            new DriveClassification(DriveMediaType.HDD, DriveInterfaceType.SATA),
            new DriveClassification(DriveMediaType.SSD, DriveInterfaceType.NVMe),
            sameVolume: false);

        Assert.False(strategy.Settings.BatchOrderByFileSize);
    }

    [Fact]
    public void RoutingEnabled_SsdSourceToHdd_KeepsBatchOrderBySize()
    {
        var strategy = Resolve(
            new OperationalSettings
            {
                DestinationRoutingEnabled = true,
                BatchOrderByFileSize = true,
            },
            new DriveClassification(DriveMediaType.SSD, DriveInterfaceType.NVMe),
            new DriveClassification(DriveMediaType.HDD, DriveInterfaceType.SATA),
            sameVolume: false);

        Assert.True(strategy.Settings.BatchOrderByFileSize);
    }

    [Fact]
    public void RoutingDisabled_SameVolumeHdd_KeepsRequestedBatchOrder()
    {
        var strategy = Resolve(
            new OperationalSettings
            {
                DestinationRoutingEnabled = false,
                BatchOrderByFileSize = true,
            },
            new DriveClassification(DriveMediaType.HDD, DriveInterfaceType.SATA),
            new DriveClassification(DriveMediaType.HDD, DriveInterfaceType.SATA),
            sameVolume: true);

        Assert.True(strategy.Settings.BatchOrderByFileSize);
    }

    [Theory]
    [InlineData(DriveMediaType.SSD, DriveInterfaceType.NVMe, DriveMediaType.MTP, DriveInterfaceType.USB)]
    [InlineData(DriveMediaType.MTP, DriveInterfaceType.USB, DriveMediaType.SSD, DriveInterfaceType.NVMe)]
    public void MtpEndpoint_DisablesBatchingAndSizeOrdering(
        DriveMediaType srcMedia, DriveInterfaceType srcIface,
        DriveMediaType dstMedia, DriveInterfaceType dstIface)
    {
        var strategy = Resolve(
            new OperationalSettings
            {
                BatchBufferBytes = 1024 * 1024,
                BatchOrderByFileSize = true,
                DestinationRoutingEnabled = true,
            },
            new DriveClassification(srcMedia, srcIface),
            new DriveClassification(dstMedia, dstIface));

        Assert.IsType<StreamingCopyStrategy>(strategy);
        Assert.Equal(0, strategy.Settings.BatchBufferBytes);
        Assert.False(strategy.Settings.BatchOrderByFileSize);
    }

    private static OperationalSettings RoutedSettings() => new()
    {
        CopyBufferSizeBytes = BaseBuffer,
        DestinationRoutingEnabled = true,
        CopyBufferRouting = new CopyBufferRoutingSettings
        {
            SsdBytes = SsdBuffer,
            UsbBytes = UsbBuffer,
            HddBytes = HddBuffer,
            SameVolumeHddBytes = SameVolumeHddBuffer,
            UnknownBytes = UnknownBuffer,
        },
    };
}
