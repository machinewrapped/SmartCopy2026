using SmartCopy.Core.FileSystem;
using SmartCopy.Core.FileSystem.Hardware;
using SmartCopy.Core.Pipeline.Strategy;

namespace SmartCopy.Tests.Pipeline;

/// <summary>
/// Tests the per-file write-durability decision made by the copy strategy: tiny files (at/below the
/// threshold) request Direct, larger files request Staged, and a target that cannot stage always
/// gets Direct.
/// </summary>
public sealed class CopyDurabilityTests
{
    private const long Threshold = 64 * 1024;

    [Theory] // targetSupportsStaging, threshold, fileSize, expected
    [InlineData(true, Threshold, 1L, WriteDurability.Direct)]               // tiny → direct
    [InlineData(true, Threshold, Threshold, WriteDurability.Direct)]        // boundary inclusive
    [InlineData(true, Threshold, Threshold + 1, WriteDurability.Staged)]    // above threshold → staged
    [InlineData(true, 0L, 1L, WriteDurability.Staged)]                      // threshold disabled → always staged
    [InlineData(false, Threshold, 1L, WriteDurability.Direct)]              // can't stage → direct
    [InlineData(false, Threshold, Threshold + 1, WriteDurability.Direct)]   // can't stage → direct even when large
    public void ResolvesPerFileDurability(bool targetSupportsStaging, long threshold, long fileSize, WriteDurability expected)
    {
        var settings = new OperationalSettings { TinyFileFastPathThresholdBytes = threshold };
        var targetCaps = ProviderCapabilities.Full with { AllowStagedWrite = targetSupportsStaging };
        var strategy = (CopyStrategyBase)DefaultCopyStrategyPolicy.Instance.Resolve(new CopyStrategyInputs(
            settings, DriveClassification.Unknown, DriveClassification.Unknown,
            ProviderCapabilities.Full, targetCaps, SameVolume: false));

        Assert.Equal(expected, strategy.SettingsFor(fileSize).WriteDurability);
    }
}
