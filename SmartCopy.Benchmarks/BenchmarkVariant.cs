using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Settings;

namespace SmartCopy.Benchmarks;

internal sealed class BenchmarkVariant
{
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool Enabled { get; set; } = true;
    public int DesiredRunCount { get; set; } = 3;
    public bool WriteJournal { get; set; } = true;
    public OverwriteMode? OverwriteMode { get; set; }
    public int? ProviderCopyBufferSizeBytes { get; set; }
    public long? ProviderSmallFileProgressThresholdBytes { get; set; }
    public long? DirectWriteThresholdBytes { get; set; }
    public long? BufferBatchBytes { get; set; }
    public long? BatchEligibilityThresholdBytes { get; set; }
    public bool? DestinationRoutingEnabled { get; set; }
    public bool? ProviderWriteSequentialScan { get; set; }
    /// <summary>
    /// Overrides the mode default executor. Null keeps the mode default (benchmark=prototype,
    /// validation=production); true forces the prototype executor; false forces the production runner.
    /// </summary>
    public bool? UsePrototypeExecutor { get; set; }
    public string? MatchedControl { get; set; }
    public int? ExpectedEffectiveCopyBufferSizeBytes { get; set; }
    public long? ExpectedEffectiveBatchBufferBytes { get; set; }
    public long? ExpectedEffectiveBatchEligibilityCeilingBytes { get; set; }
    public long? ExpectedEffectiveTinyFileFastPathThresholdBytes { get; set; }
    public bool? ExpectedEffectiveDestinationRoutingEnabled { get; set; }

    /// <summary>
    /// Describes what the <see cref="MatchedControl"/> comparison is for, so <c>--mode validation</c>
    /// can word its conclusion: <c>"Equivalence"</c> (this variant should match its control — parity),
    /// <c>"Value"</c> (this variant should beat its control — a gain). Null/other reads as a neutral
    /// comparison. Does not change the pass rule: a pair fails only on REGRESSION or INVALID.
    /// </summary>
    public string? ValidationRole { get; set; }

    public void Normalize()
    {
        Name = Name.Trim();
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("benchmark variant name is required.");
        }

        if (DesiredRunCount <= 0)
        {
            throw new InvalidOperationException($"benchmark variant '{Name}' must have desiredRunCount > 0.");
        }
    }

    public OperationalSettings CreateOperationalSettings(BenchmarkScenario scenario)
    {
        var defaults = new OperationalSettings();
        return new OperationalSettings
        {
            CopyBufferSizeBytes = ProviderCopyBufferSizeBytes
                ?? scenario.ProviderCopyBufferSizeBytes
                ?? defaults.CopyBufferSizeBytes,
            SmallFileProgressThresholdBytes = ProviderSmallFileProgressThresholdBytes
                ?? scenario.ProviderSmallFileProgressThresholdBytes
                ?? defaults.SmallFileProgressThresholdBytes,
        }.Normalize();
    }

    /// <summary>
    /// Builds <see cref="OperationalSettings"/> for the production copy path (PipelineRunner →
    /// DefaultCopyStrategyPolicy → BatchedCopyStrategy/StreamingCopyStrategy), mapping the
    /// variant/scenario batch/direct/routing fields onto the engine-facing settings. Used by
    /// <c>--mode validation</c>. The legacy <see cref="CreateOperationalSettings"/> is left for
    /// the prototype path (<c>BenchmarkCopyRunner</c>), which consumes these fields as separate
    /// arguments instead of through <see cref="OperationalSettings"/>.
    /// </summary>
    public OperationalSettings CreateProductionOperationalSettings(BenchmarkScenario scenario)
    {
        var defaults = new AppSettings().CreateOperationalSettings();
        var batchBufferBytes = BufferBatchBytes ?? scenario.BufferBatchBytes ?? defaults.BatchBufferBytes;
        var batchEligibilityCeilingBytes = BatchEligibilityThresholdBytes ?? scenario.BatchEligibilityThresholdBytes ?? defaults.BatchEligibilityCeilingBytes;
        var tinyFileFastPathThresholdBytes = DirectWriteThresholdBytes ?? scenario.DirectWriteThresholdBytes ?? defaults.TinyFileFastPathThresholdBytes;
        var destinationRoutingEnabled = DestinationRoutingEnabled ?? defaults.DestinationRoutingEnabled;

        return new OperationalSettings
        {
            CopyBufferSizeBytes = ProviderCopyBufferSizeBytes
                ?? scenario.ProviderCopyBufferSizeBytes
                ?? defaults.CopyBufferSizeBytes,
            SmallFileProgressThresholdBytes = ProviderSmallFileProgressThresholdBytes
                ?? scenario.ProviderSmallFileProgressThresholdBytes
                ?? defaults.SmallFileProgressThresholdBytes,
            BatchBufferBytes = batchBufferBytes,
            BatchEligibilityCeilingBytes = batchEligibilityCeilingBytes,
            TinyFileFastPathThresholdBytes = tinyFileFastPathThresholdBytes,
            DestinationRoutingEnabled = destinationRoutingEnabled,
        }.Normalize();
    }

}
