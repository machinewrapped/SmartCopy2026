using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;

namespace SmartCopy.Benchmarks;

internal sealed class BenchmarkScenario
{
    public string Name { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public string? SourcePath { get; set; }
    public bool Enabled { get; set; } = true;
    public bool ClearDestinationBeforeRun { get; set; } = true;
    public bool ClearDestinationAfterRun { get; set; } = true;
    public OverwriteMode OverwriteMode { get; set; } = OverwriteMode.Always;
    public int? ProviderCopyBufferSizeBytes { get; set; }
    public long? ProviderSmallFileProgressThresholdBytes { get; set; }
    public LocalFileSystemWriteMode? ProviderWriteMode { get; set; }
    public bool? ProviderUseArrayPoolForManualLoop { get; set; }
    public long? DirectWriteThresholdBytes { get; set; }
    public long? BufferBatchBytes { get; set; }
    public long? BatchEligibilityThresholdBytes { get; set; }
    public bool? ProviderWriteSequentialScan { get; set; }
    public bool UsePathPool { get; set; }
    public List<string>? Variants { get; set; }


    public void Normalize()
    {
        Name = Name.Trim();
        DestinationPath = Path.GetFullPath(DestinationPath);
        if (!string.IsNullOrWhiteSpace(SourcePath))
        {
            SourcePath = Path.GetFullPath(SourcePath);
        }
    }

    public OperationalSettings CreateOperationalSettings()
    {
        var defaults = new OperationalSettings();
        return new OperationalSettings
        {
            CopyBufferSizeBytes = ProviderCopyBufferSizeBytes ?? defaults.CopyBufferSizeBytes,
            SmallFileProgressThresholdBytes = ProviderSmallFileProgressThresholdBytes
                ?? defaults.SmallFileProgressThresholdBytes,
            WriteMode = ProviderWriteMode ?? defaults.WriteMode,
            UseArrayPoolForManualLoop = ProviderUseArrayPoolForManualLoop
                ?? defaults.UseArrayPoolForManualLoop,
        }.Normalize();
    }
}

