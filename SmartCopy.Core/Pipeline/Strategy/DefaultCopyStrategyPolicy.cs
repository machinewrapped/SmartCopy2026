using SmartCopy.Core.FileSystem;
using SmartCopy.Core.FileSystem.Hardware;

namespace SmartCopy.Core.Pipeline.Strategy;

/// <summary>
/// Static, evidence-based copy-strategy policy. Always clamps to provider capabilities; when
/// <see cref="OperationalSettings.DestinationRoutingEnabled"/> is set, also selects the copy
/// buffer size from the source→destination drive pair per the validated benchmark findings
/// (see <c>Docs/optimisation-strategies.md</c> Sections 2.5.3 / 2.6.3). Preallocation is OFF
/// universally. This is the seam future per-device learned profiles replace.
/// </summary>
public sealed class DefaultCopyStrategyPolicy : ICopyStrategyPolicy
{
    public static readonly DefaultCopyStrategyPolicy Instance = new();

    /// <summary>Buffer for fast destinations (SSD/NVMe and USB removable). Section 2.5.3 / 2.6.3.</summary>
    public const int FastBufferBytes = 1024 * 1024;

    /// <summary>Conservative buffer for HDD-limited or unclassified pairs. Section 2.5.3.</summary>
    public const int ConservativeBufferBytes = 512 * 1024;

    public ICopyStrategy Resolve(CopyStrategyInputs inputs)
    {
        var resolved = inputs.Base.WithProviderConstraints(inputs.SourceCaps, inputs.TargetCaps);

        if (resolved.DestinationRoutingEnabled)
        {
            resolved = resolved with
            {
                CopyBufferSizeBytes = SelectBufferBytes(inputs.Source, inputs.Target),
                PreallocateDestinationFile = false,
            };
        }

        var targetSupportsStaging = inputs.TargetCaps.AllowStagedWrite;
        return resolved.BatchBufferBytes > 0
            ? new BatchedCopyStrategy(resolved, targetSupportsStaging)
            : new StreamingCopyStrategy(resolved, targetSupportsStaging);
    }

    /// <summary>
    /// Picks the copy buffer size from the drive pair. The limiting device decides:
    /// any HDD on the pair caps throughput regardless of buffer (Section 2.5 findings #3/#4),
    /// so the conservative buffer is used; fast pairs and USB use the larger buffer.
    /// </summary>
    internal static int SelectBufferBytes(DriveClassification source, DriveClassification target)
    {
        // USB / removable destination: 1 MiB is the validated prior (Section 2.6.3).
        if (target.InterfaceType == DriveInterfaceType.USB)
            return FastBufferBytes;

        // Any HDD on either side caps throughput — stay conservative (Section 2.5 findings #3/#4).
        if (source.MediaType == DriveMediaType.HDD || target.MediaType == DriveMediaType.HDD)
            return ConservativeBufferBytes;

        // SSD↔SSD: 1 MiB is the safe universal default (Section 2.5.3). Same-volume SSD may
        // benefit from a larger buffer but that probe (Phase 5 Step 3) is unrun, so stay at 1 MiB.
        if (source.MediaType == DriveMediaType.SSD && target.MediaType == DriveMediaType.SSD)
            return FastBufferBytes;

        // Unknown / Memory / MTP / ambiguous: do not assume fast media.
        return ConservativeBufferBytes;
    }
}
