using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;

namespace SmartCopy.Benchmarks;

internal sealed class BenchmarkVariant
{
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool Enabled { get; set; } = true;
    public int DesiredRunCount { get; set; } = 3;
    public OverwriteMode? OverwriteMode { get; set; }
    public int? ProviderCopyBufferSizeBytes { get; set; }
    public long? ProviderSmallFileProgressThresholdBytes { get; set; }
    public LocalFileSystemWriteMode? ProviderWriteMode { get; set; }
    public bool? ProviderUseArrayPoolForManualLoop { get; set; }
    public bool? ProviderPreallocateDestinationFile { get; set; }
    public long? DirectWriteThresholdBytes { get; set; }
    public bool? SkipExistsCheckForOverwrite { get; set; }
    public long? BufferBatchBytes { get; set; }
    public string? MatchedControl { get; set; }

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
            WriteMode = ProviderWriteMode
                ?? scenario.ProviderWriteMode
                ?? defaults.WriteMode,
            UseArrayPoolForManualLoop = ProviderUseArrayPoolForManualLoop
                ?? scenario.ProviderUseArrayPoolForManualLoop
                ?? defaults.UseArrayPoolForManualLoop,
            PreallocateDestinationFile = ProviderPreallocateDestinationFile
                ?? scenario.ProviderPreallocateDestinationFile
                ?? defaults.PreallocateDestinationFile,
        }.Normalize();
    }
}

