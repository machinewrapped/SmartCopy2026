using Microsoft.Extensions.Logging;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.FileSystem.Hardware;
using SmartCopy.Core.Logging;

namespace SmartCopy.Core.Pipeline.Strategy;

/// <summary>
/// Static, evidence-based copy-strategy policy. Always clamps to provider capabilities; when
/// <see cref="OperationalSettings.DestinationRoutingEnabled"/> is set, also selects the copy
/// buffer size from the configurable routing table on <see cref="OperationalSettings"/>. The
/// baseline table keeps same-volume HDD at 256 KiB, routes cross-volume HDD and unknown media to
/// 512 KiB, and routes SSD/USB pairs to 1 MiB. Batching always preserves natural source order and
/// flushes at capacity, directory exit, and selection end. This is the seam future per-device
/// learned profiles replace.
/// </summary>
public sealed class DefaultCopyStrategyPolicy : ICopyStrategyPolicy
{
    public static readonly DefaultCopyStrategyPolicy Instance = new();

    private readonly ILogger<DefaultCopyStrategyPolicy> _logger = AppLog.CreateLogger<DefaultCopyStrategyPolicy>();

    public ICopyStrategy Resolve(CopyStrategyInputs inputs)
    {
        var resolved = inputs.Base.WithProviderConstraints(inputs.SourceCaps, inputs.TargetCaps);

        // MTP protocol transfers are serialized and do not benefit from read-side batching.
        var isMtpTransfer = IsMtpTransfer(inputs.Source, inputs.Target);
        if (isMtpTransfer)
            resolved = resolved with { BatchBufferBytes = 0 };

        if (resolved.DestinationRoutingEnabled)
        {
            resolved = resolved with
            {
                CopyBufferSizeBytes = SelectBufferBytes(
                    inputs.Source,
                    inputs.Target,
                    inputs.SameVolume,
                    resolved),
            };
        }

        var targetSupportsStaging = inputs.TargetCaps.AllowStagedWrite;
        ICopyStrategy strategy = resolved.BatchBufferBytes > 0
            ? new BatchedCopyStrategy(resolved, targetSupportsStaging)
            : new StreamingCopyStrategy(resolved, targetSupportsStaging);

        // One line per step (not per file) describing the resolved decision; enable Debug to see it.
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Copy strategy resolved: {Strategy} buffer={BufferKiB}KiB batch={BatchKiB}KiB " +
                "durabilityThreshold={ThresholdKiB}KiB (source={Source}, target={Target}, " +
                "sameVolume={SameVolume}, routing={Routing}, targetStaging={Staging})",
                strategy.GetType().Name,
                resolved.CopyBufferSizeBytes / 1024,
                resolved.BatchBufferBytes / 1024,
                resolved.TinyFileFastPathThresholdBytes / 1024,
                inputs.Source, inputs.Target, inputs.SameVolume,
                resolved.DestinationRoutingEnabled, targetSupportsStaging);
        }

        return strategy;
    }

    /// <summary>
    /// Picks the copy buffer size from the drive pair using the settings-backed baseline table.
    /// Same-volume HDD is intentionally separate from cross-volume HDD because the source and
    /// destination contend for the same spindle.
    /// </summary>
    internal static int SelectBufferBytes(
        DriveClassification source,
        DriveClassification target,
        bool sameVolume,
        OperationalSettings settings)
    {
        // Same-volume HDD contends for one spindle; profiling canonises 256 KiB as the baseline.
        if (sameVolume && IsHddPair(source, target))
            return settings.CopyBufferRouting.SameVolumeHddBytes;

        // USB / removable destination: 1 MiB is the validated prior outside same-volume HDD.
        if (target.InterfaceType == DriveInterfaceType.USB)
            return settings.CopyBufferRouting.UsbBytes;

        // Cross-volume HDD remains conservative.
        if (IsHddPair(source, target))
            return settings.CopyBufferRouting.HddBytes;

        // SSD↔SSD: 1 MiB is the safe universal default. Same-volume SSD may benefit from a larger
        // buffer but that probe is unrun, so stay at 1 MiB.
        if (source.MediaType == DriveMediaType.SSD && target.MediaType == DriveMediaType.SSD)
            return settings.CopyBufferRouting.SsdBytes;

        // Unknown / Memory / MTP / ambiguous: do not assume fast media.
        return settings.CopyBufferRouting.UnknownBytes;
    }

    private static bool IsMtpTransfer(DriveClassification source, DriveClassification target) =>
        source.MediaType == DriveMediaType.MTP || target.MediaType == DriveMediaType.MTP;

    private static bool IsHddPair(DriveClassification source, DriveClassification target) =>
        source.MediaType == DriveMediaType.HDD || target.MediaType == DriveMediaType.HDD;

}
