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
    private static ICopyStrategy Resolve(OperationalSettings settings, DriveClassification source, DriveClassification target) =>
        DefaultCopyStrategyPolicy.Instance.Resolve(new CopyStrategyInputs(
            settings, source, target, ProviderCapabilities.Full, ProviderCapabilities.Full, SameVolume: false));

    [Theory] // the benchmark-validated routing table: 1 MiB for SSD/USB, 512 KiB where an HDD caps the pair
    [InlineData(DriveMediaType.SSD, DriveInterfaceType.NVMe, DriveMediaType.SSD, DriveInterfaceType.NVMe, DefaultCopyStrategyPolicy.FastBufferBytes)]          // SSD↔SSD
    [InlineData(DriveMediaType.SSD, DriveInterfaceType.NVMe, DriveMediaType.SSD, DriveInterfaceType.USB, DefaultCopyStrategyPolicy.FastBufferBytes)]           // USB target
    [InlineData(DriveMediaType.SSD, DriveInterfaceType.NVMe, DriveMediaType.HDD, DriveInterfaceType.SATA, DefaultCopyStrategyPolicy.ConservativeBufferBytes)]  // HDD target
    [InlineData(DriveMediaType.HDD, DriveInterfaceType.SATA, DriveMediaType.SSD, DriveInterfaceType.NVMe, DefaultCopyStrategyPolicy.ConservativeBufferBytes)]  // HDD source
    [InlineData(DriveMediaType.Unknown, DriveInterfaceType.Unknown, DriveMediaType.Unknown, DriveInterfaceType.Unknown, DefaultCopyStrategyPolicy.ConservativeBufferBytes)]
    public void RoutingEnabled_SelectsBufferFromDrivePair(
        DriveMediaType srcMedia, DriveInterfaceType srcIface,
        DriveMediaType dstMedia, DriveInterfaceType dstIface, int expectedBuffer)
    {
        var strategy = Resolve(
            new OperationalSettings { DestinationRoutingEnabled = true },
            new DriveClassification(srcMedia, srcIface), new DriveClassification(dstMedia, dstIface));

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
    public void RoutingEnabled_ForcesPreallocationOff()
    {
        var strategy = Resolve(
            new OperationalSettings { DestinationRoutingEnabled = true, PreallocateDestinationFile = true },
            new DriveClassification(DriveMediaType.SSD, DriveInterfaceType.NVMe),
            new DriveClassification(DriveMediaType.SSD, DriveInterfaceType.NVMe));

        Assert.False(strategy.Settings.PreallocateDestinationFile);
    }
}
