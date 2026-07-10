using SmartCopy.Core.FileSystem;
using SmartCopy.Core.FileSystem.Hardware;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Strategy;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

/// <summary>
/// Tests the <see cref="IStepContext.ResolveCopyStrategyAsync"/> seam: it must fetch the
/// source/target drive classifications and route the buffer through the policy when enabled.
/// </summary>
public sealed class CopyStrategySeamTests
{
    private const int RoutedUsbBuffer = 123 * 1024;

    // The one behaviour nothing else covers: the seam fetches the source and target classifications
    // and forwards them to the policy — in the right slots. The pair is deliberately ASYMMETRIC:
    // "target interface == USB" is the only routing rule that distinguishes source from target, so an
    // HDD source + USB-attached SSD target routes to the fast buffer, but a source/target swap (or
    // copy-pasting target's classification into both slots) makes target read as HDD → conservative,
    // and fails. A symmetric SSD↔SSD pair would silently pass those faults. Policy/durability tests
    // call Resolve directly (bypassing the seam); integration tests run routing-off (classifications
    // ignored) — neither would catch a mix-up here.
    [Fact]
    public async Task ResolveCopyStrategyAsync_ForwardsSourceAndTargetClassifications_InCorrectSlots()
    {
        var (ctx, target) = await BuildAsync(
            sourceClass: new(DriveMediaType.HDD, DriveInterfaceType.SATA),
            targetClass: new(DriveMediaType.SSD, DriveInterfaceType.USB),
            routing: true,
            sourceVolumeId: "source-volume",
            targetVolumeId: "target-volume");

        var strategy = await ctx.ResolveCopyStrategyAsync(target, CancellationToken.None);

        Assert.Equal(RoutedUsbBuffer, strategy.Settings.CopyBufferSizeBytes);
    }

    [Fact]
    public async Task ResolveCopyStrategyAsync_ForwardsVolumeIdsAndSameVolume()
    {
        var policy = new CapturingPolicy();
        var (ctx, target) = await BuildAsync(
            sourceClass: new(DriveMediaType.HDD, DriveInterfaceType.SATA),
            targetClass: new(DriveMediaType.HDD, DriveInterfaceType.SATA),
            routing: true,
            sourceVolumeId: "volume-a",
            targetVolumeId: "volume-a",
            policy: policy);

        await ctx.ResolveCopyStrategyAsync(target, CancellationToken.None);

        Assert.True(policy.Captured.HasValue);
        var inputs = policy.Captured.Value;
        Assert.True(inputs.SameVolume);
        Assert.Equal("volume-a", inputs.SourceVolumeId);
        Assert.Equal("volume-a", inputs.TargetVolumeId);
    }

    private static async Task<(IStepContext ctx, IFileSystemProvider target)> BuildAsync(
        DriveClassification sourceClass,
        DriveClassification targetClass,
        bool routing,
        string? sourceVolumeId = null,
        string? targetVolumeId = null,
        ICopyStrategyPolicy? policy = null)
    {
        var memory = MemoryFileSystemFixtures.Create(f => f.WithFile("/src/a.txt", "x"u8).WithDirectory("/dest"));
        var root = await memory.BuildDirectoryTree("/src");

        var source = new CapabilityOverrideProvider(memory, ProviderCapabilities.Full, sourceClass, sourceVolumeId);
        var target = new CapabilityOverrideProvider(memory, ProviderCapabilities.Full, targetClass, targetVolumeId);
        var settings = new OperationalSettings
        {
            DestinationRoutingEnabled = routing,
            CopyBufferRouting = new CopyBufferRoutingSettings
            {
                UsbBytes = RoutedUsbBuffer,
            },
        };

        return (new FakeStepContext(root, source, settings: settings, copyStrategyPolicy: policy), target);
    }

    private sealed class CapturingPolicy : ICopyStrategyPolicy
    {
        public CopyStrategyInputs? Captured { get; private set; }

        public ICopyStrategy Resolve(CopyStrategyInputs inputs)
        {
            Captured = inputs;
            return DefaultCopyStrategyPolicy.Instance.Resolve(inputs);
        }
    }
}
