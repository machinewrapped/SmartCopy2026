using System.Runtime.InteropServices;
using System.Text.Json;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;

namespace SmartCopy.Benchmarks;

internal enum BenchmarkRunMode
{
    Benchmark,
    DatasetPreparation,
    Analysis,
    SizeScaling,
}

internal sealed class BenchmarkCliOptions
{
    public BenchmarkRunMode Mode { get; init; } = BenchmarkRunMode.Benchmark;
    public const string DefaultConfigFileName = "benchmark-scenarios.json";

    public string? ScenarioName { get; init; }
    public string? VariantName { get; init; }
    public string? Notes { get; init; }
    public string ConfigPath { get; init; } = DefaultConfigFileName;

    public static BenchmarkCliOptions Parse(string[] args)
    {
        var mode = BenchmarkRunMode.Benchmark;
        string? scenarioName = null;
        string? variantName = null;
        string? notes = null;
        var configPath = DefaultConfigFileName;

        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--scenario", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                scenarioName = args[++i];
            }
            else if (string.Equals(args[i], "--variant", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                variantName = args[++i];
            }
            else if (string.Equals(args[i], "--notes", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                notes = args[++i];
            }
            else if (string.Equals(args[i], "--mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                mode = ParseMode(args[++i]);
            }
            else if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                configPath = args[++i];
            }
        }

        return new BenchmarkCliOptions
        {
            Mode = mode,
            ScenarioName = scenarioName,
            VariantName = variantName,
            Notes = notes,
            ConfigPath = configPath,
        };
    }

    private static BenchmarkRunMode ParseMode(string value)
    {
        if (string.Equals(value, "benchmark", StringComparison.OrdinalIgnoreCase))
        {
            return BenchmarkRunMode.Benchmark;
        }

        if (string.Equals(value, "dataset-prep", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "dataset-preparation", StringComparison.OrdinalIgnoreCase))
        {
            return BenchmarkRunMode.DatasetPreparation;
        }

        if (string.Equals(value, "analysis", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "analyze", StringComparison.OrdinalIgnoreCase))
        {
            return BenchmarkRunMode.Analysis;
        }

        if (string.Equals(value, "size-scaling", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "scaling", StringComparison.OrdinalIgnoreCase))
        {
            return BenchmarkRunMode.SizeScaling;
        }

        throw new InvalidOperationException($"Unknown benchmark mode '{value}'. Expected 'benchmark', 'dataset-prep', 'analysis', or 'size-scaling'.");
    }
}

internal sealed class BenchmarkConfig
{
    public string SourcePath { get; set; } = @"R:\TestData\MixedDataset";
    public string? ArtifactPath { get; set; }
    public bool IncludeHidden { get; set; }
    public List<BenchmarkScenario> Scenarios { get; set; } = [];
    public List<string> ScenarioExecutionOrder { get; set; } = [];
    public List<BenchmarkVariant> Variants { get; set; } = [];
    public DatasetPreparationConfig? DatasetPreparation { get; set; }

    public static BenchmarkConfig CreateTemplate() =>
        new()
        {
            Scenarios =
            [
                new BenchmarkScenario { Name = "SameDriveTest", DestinationPath = @"R:\TestData\SameDriveTest" },
                new BenchmarkScenario { Name = "SSDtoSSD", DestinationPath = @"D:\TestData\SSDtoSSD" },
                new BenchmarkScenario { Name = "SSDtoHDD", DestinationPath = @"L:\TestData\SSDtoHDD" },
                new BenchmarkScenario { Name = "SSDtoUSBFlash", DestinationPath = @"T:\TestData\SSDtoUSBFlash" },
            ],
            ScenarioExecutionOrder =
            [
                "SSDtoSSD",
                "SameDriveTest",
                "SSDtoHDD",
                "SSDtoUSBFlash",
            ],
            Variants =
            [
                new BenchmarkVariant
                {
                    Name = "BaselineAuto",
                    Notes = "Current heuristic defaults.",
                    DesiredRunCount = 2,
                },
                new BenchmarkVariant
                {
                    Name = "CopyToAsync512KiB",
                    Notes = "Always uses Stream.CopyToAsync with a 512 KiB buffer.",
                    DesiredRunCount = 2,
                    ProviderCopyBufferSizeBytes = 512 * 1024,
                    ProviderWriteMode = LocalFileSystemWriteMode.CopyToAsync,
                },
                new BenchmarkVariant
                {
                    Name = "ManualLoop512KiBArrayPool",
                    Notes = "Manual loop with a 512 KiB buffer and pooled buffers.",
                    DesiredRunCount = 2,
                    ProviderCopyBufferSizeBytes = 512 * 1024,
                    ProviderWriteMode = LocalFileSystemWriteMode.ManualLoop,
                    ProviderUseArrayPoolForManualLoop = true,
                },
                new BenchmarkVariant
                {
                    Name = "ManualLoop1MiBPreallocate",
                    Notes = "Manual loop with a 1 MiB buffer and destination preallocation.",
                    DesiredRunCount = 2,
                    ProviderCopyBufferSizeBytes = 1024 * 1024,
                    ProviderWriteMode = LocalFileSystemWriteMode.ManualLoop,
                    ProviderUseArrayPoolForManualLoop = true,
                    ProviderPreallocateDestinationFile = true,
                },
            ],
            DatasetPreparation = new DatasetPreparationConfig
            {
                SourcePath = @"R:\CandidateData",
                DestinationPath = @"R:\TestData\MixedDataset",
                Buckets =
                [
                    new DatasetPreparationBucketConfig
                    {
                        Name = "Tiny",
                        MinimumFileSizeBytes = 0,
                        MaximumFileSizeBytes = 64 * 1024,
                        TargetTotalBytes = 256L * 1024 * 1024,
                    },
                    new DatasetPreparationBucketConfig
                    {
                        Name = "Small",
                        MinimumFileSizeBytes = 64 * 1024 + 1,
                        MaximumFileSizeBytes = 512 * 1024,
                        TargetTotalBytes = 512L * 1024 * 1024,
                    },
                    new DatasetPreparationBucketConfig
                    {
                        Name = "Medium",
                        MinimumFileSizeBytes = 512 * 1024 + 1,
                        MaximumFileSizeBytes = 4 * 1024 * 1024,
                        TargetTotalBytes = 2L * 1024 * 1024 * 1024,
                    },
                    new DatasetPreparationBucketConfig
                    {
                        Name = "Large",
                        MinimumFileSizeBytes = 4 * 1024 * 1024 + 1,
                        MaximumFileSizeBytes = 32 * 1024 * 1024,
                        TargetTotalBytes = 3L * 1024 * 1024 * 1024,
                    },
                    new DatasetPreparationBucketConfig
                    {
                        Name = "XLarge",
                        MinimumFileSizeBytes = 32 * 1024 * 1024 + 1,
                        MaximumFileSizeBytes = 256 * 1024 * 1024,
                        TargetTotalBytes = 4L * 1024 * 1024 * 1024,
                    },
                    new DatasetPreparationBucketConfig
                    {
                        Name = "Huge",
                        MinimumFileSizeBytes = 256 * 1024 * 1024 + 1,
                        MaximumFileSizeBytes = 2L * 1024 * 1024 * 1024,
                        TargetTotalBytes = 4L * 1024 * 1024 * 1024,
                    },
                ],
            },
        };

    public void Normalize()
    {
        SourcePath = Path.GetFullPath(SourcePath);
        ArtifactPath = string.IsNullOrWhiteSpace(ArtifactPath)
            ? null
            : Path.GetFullPath(ArtifactPath);

        foreach (var scenario in Scenarios)
        {
            scenario.Normalize();
        }

        ScenarioExecutionOrder = ScenarioExecutionOrder
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (Variants.Count == 0)
        {
            Variants.Add(new BenchmarkVariant
            {
                Name = "ScenarioDefaults",
                Notes = "Synthesized for legacy configs without a variants section.",
                DesiredRunCount = 1,
            });
        }

        foreach (var variant in Variants)
        {
            variant.Normalize();
        }

        DatasetPreparation?.Normalize();
    }
}

internal sealed class BenchmarkScenario
{
    public string Name { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public string? SourcePath { get; set; }
    public bool Enabled { get; set; } = true;
    public bool ClearDestinationBeforeRun { get; set; } = true;
    public OverwriteMode OverwriteMode { get; set; } = OverwriteMode.Always;
    public int? ProviderCopyBufferSizeBytes { get; set; }
    public long? ProviderSmallFileProgressThresholdBytes { get; set; }
    public LocalFileSystemWriteMode? ProviderWriteMode { get; set; }
    public bool? ProviderUseArrayPoolForManualLoop { get; set; }
    public bool? ProviderPreallocateDestinationFile { get; set; }
    public long? DirectWriteThresholdBytes { get; set; }
    public bool? SkipExistsCheckForOverwrite { get; set; }
    public long? BufferBatchBytes { get; set; }
    public bool UsePathPool { get; set; }


    public void Normalize()
    {
        Name = Name.Trim();
        DestinationPath = Path.GetFullPath(DestinationPath);
        if (!string.IsNullOrWhiteSpace(SourcePath))
        {
            SourcePath = Path.GetFullPath(SourcePath);
        }
    }

    public LocalFileSystemProviderOptions CreateProviderOptions()
    {
        return new LocalFileSystemProviderOptions
        {
            CopyBufferSizeBytes = ProviderCopyBufferSizeBytes ?? LocalFileSystemProviderOptions.Default.CopyBufferSizeBytes,
            SmallFileProgressThresholdBytes = ProviderSmallFileProgressThresholdBytes
                ?? LocalFileSystemProviderOptions.Default.SmallFileProgressThresholdBytes,
            WriteMode = ProviderWriteMode ?? LocalFileSystemProviderOptions.Default.WriteMode,
            UseArrayPoolForManualLoop = ProviderUseArrayPoolForManualLoop
                ?? LocalFileSystemProviderOptions.Default.UseArrayPoolForManualLoop,
            PreallocateDestinationFile = ProviderPreallocateDestinationFile
                ?? LocalFileSystemProviderOptions.Default.PreallocateDestinationFile,
        }.Normalize();
    }
}

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

    public LocalFileSystemProviderOptions CreateProviderOptions(BenchmarkScenario scenario)
    {
        return new LocalFileSystemProviderOptions
        {
            CopyBufferSizeBytes = ProviderCopyBufferSizeBytes
                ?? scenario.ProviderCopyBufferSizeBytes
                ?? LocalFileSystemProviderOptions.Default.CopyBufferSizeBytes,
            SmallFileProgressThresholdBytes = ProviderSmallFileProgressThresholdBytes
                ?? scenario.ProviderSmallFileProgressThresholdBytes
                ?? LocalFileSystemProviderOptions.Default.SmallFileProgressThresholdBytes,
            WriteMode = ProviderWriteMode
                ?? scenario.ProviderWriteMode
                ?? LocalFileSystemProviderOptions.Default.WriteMode,
            UseArrayPoolForManualLoop = ProviderUseArrayPoolForManualLoop
                ?? scenario.ProviderUseArrayPoolForManualLoop
                ?? LocalFileSystemProviderOptions.Default.UseArrayPoolForManualLoop,
            PreallocateDestinationFile = ProviderPreallocateDestinationFile
                ?? scenario.ProviderPreallocateDestinationFile
                ?? LocalFileSystemProviderOptions.Default.PreallocateDestinationFile,
        }.Normalize();
    }
}

internal sealed class BenchmarkState
{
    public DirectoryNode? Root { get; set; }
    public IReadOnlyList<TransformResult> Results { get; set; } = [];
    public long? FreeSpaceBefore { get; set; }
    public long? FreeSpaceAfter { get; set; }
    public string? JournalPath { get; set; }
    public System.Diagnostics.Stopwatch ScanStopwatch { get; } = new();
    public System.Diagnostics.Stopwatch ExecuteStopwatch { get; } = new();
}

internal sealed class BenchmarkRunRecord
{
    public string RunStatus { get; init; } = BenchmarkRunStatus.Completed;
    public required string ScenarioName { get; init; }
    public required string VariantName { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public string? ArtifactPath { get; init; }
    public required DateTime RunStartedUtc { get; init; }
    public required string HostName { get; init; }
    public required string OsDescription { get; init; }
    public required string FrameworkDescription { get; init; }
    public required string? Notes { get; init; }
    public required int RunIndex { get; init; }
    public int? ProviderCopyBufferSizeBytes { get; init; }
    public long? ProviderSmallFileProgressThresholdBytes { get; init; }
    public LocalFileSystemWriteMode? ProviderWriteMode { get; init; }
    public bool? ProviderUseArrayPoolForManualLoop { get; init; }
    public bool? ProviderPreallocateDestinationFile { get; init; }
    public long? DirectWriteThresholdBytes { get; init; }
    public bool? SkipExistsCheckForOverwrite { get; init; }
    public long? BufferBatchBytes { get; init; }
    public required TimeSpan ScanDuration { get; init; }
    public required TimeSpan ExecuteDuration { get; init; }
    public required int CopiedFiles { get; init; }
    public required int SkippedFiles { get; init; }
    public required int FailedFiles { get; init; }
    public required long OutputBytes { get; init; }
    public required long? DestinationFreeSpaceBeforeBytes { get; init; }
    public required long? DestinationFreeSpaceAfterBytes { get; init; }
    public required string JournalPath { get; init; }
    public required string? ExceptionType { get; init; }
    public required string? ExceptionMessage { get; init; }
 
    public static BenchmarkRunRecord CreateInProgress(
        BenchmarkScenario scenario,
        BenchmarkVariant variant,
        string sourcePath,
        string destinationPath,
        string artifactPath,
        DateTime runStartedUtc,
        string? notes,
        int runIndex)
    {
        var providerOptions = variant.CreateProviderOptions(scenario);
        return new BenchmarkRunRecord
        {
            RunStatus = BenchmarkRunStatus.InProgress,
            ScenarioName = scenario.Name,
            VariantName = variant.Name,
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            ArtifactPath = artifactPath,
            RunStartedUtc = runStartedUtc,
            HostName = Environment.MachineName,
            OsDescription = RuntimeInformation.OSDescription,
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            Notes = notes,
            RunIndex = runIndex,
            ProviderCopyBufferSizeBytes = providerOptions.CopyBufferSizeBytes,
            ProviderSmallFileProgressThresholdBytes = providerOptions.SmallFileProgressThresholdBytes,
            ProviderWriteMode = providerOptions.WriteMode,
            ProviderUseArrayPoolForManualLoop = providerOptions.UseArrayPoolForManualLoop,
            ProviderPreallocateDestinationFile = providerOptions.PreallocateDestinationFile,
            DirectWriteThresholdBytes = variant.DirectWriteThresholdBytes ?? scenario.DirectWriteThresholdBytes,
            SkipExistsCheckForOverwrite = variant.SkipExistsCheckForOverwrite ?? scenario.SkipExistsCheckForOverwrite,
            BufferBatchBytes = variant.BufferBatchBytes ?? scenario.BufferBatchBytes,
            ScanDuration = TimeSpan.Zero,
            ExecuteDuration = TimeSpan.Zero,
            CopiedFiles = 0,
            SkippedFiles = 0,
            FailedFiles = 0,
            OutputBytes = 0,
            DestinationFreeSpaceBeforeBytes = null,
            DestinationFreeSpaceAfterBytes = null,
            JournalPath = string.Empty,
            ExceptionType = null,
            ExceptionMessage = null,
        };
    }
 
    public static BenchmarkRunRecord CreateSuccess(
        BenchmarkScenario scenario,
        BenchmarkVariant variant,
        string sourcePath,
        string destinationPath,
        string artifactPath,
        DateTime runStartedUtc,
        BenchmarkState state,
        string? notes,
        int runIndex) => Create(scenario, variant, sourcePath, destinationPath, artifactPath, runStartedUtc, state, notes, runIndex, ex: null);
 
    public static BenchmarkRunRecord CreateFailure(
        BenchmarkScenario scenario,
        BenchmarkVariant variant,
        string sourcePath,
        string destinationPath,
        string artifactPath,
        DateTime runStartedUtc,
        BenchmarkState state,
        string? notes,
        int runIndex,
        Exception ex) => Create(scenario, variant, sourcePath, destinationPath, artifactPath, runStartedUtc, state, notes, runIndex, ex);
 
    private static BenchmarkRunRecord Create(
        BenchmarkScenario scenario,
        BenchmarkVariant variant,
        string sourcePath,
        string destinationPath,
        string artifactPath,
        DateTime runStartedUtc,
        BenchmarkState state,
        string? notes,
        int runIndex,
        Exception? ex)
    {
        var providerOptions = variant.CreateProviderOptions(scenario);
        return new BenchmarkRunRecord
        {
            RunStatus = ex is null ? BenchmarkRunStatus.Completed : BenchmarkRunStatus.Failed,
            ScenarioName = scenario.Name,
            VariantName = variant.Name,
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            ArtifactPath = artifactPath,
            RunStartedUtc = runStartedUtc,
            HostName = Environment.MachineName,
            OsDescription = RuntimeInformation.OSDescription,
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            Notes = notes,
            RunIndex = runIndex,
            ProviderCopyBufferSizeBytes = providerOptions.CopyBufferSizeBytes,
            ProviderSmallFileProgressThresholdBytes = providerOptions.SmallFileProgressThresholdBytes,
            ProviderWriteMode = providerOptions.WriteMode,
            ProviderUseArrayPoolForManualLoop = providerOptions.UseArrayPoolForManualLoop,
            ProviderPreallocateDestinationFile = providerOptions.PreallocateDestinationFile,
            DirectWriteThresholdBytes = variant.DirectWriteThresholdBytes ?? scenario.DirectWriteThresholdBytes,
            SkipExistsCheckForOverwrite = variant.SkipExistsCheckForOverwrite ?? scenario.SkipExistsCheckForOverwrite,
            BufferBatchBytes = variant.BufferBatchBytes ?? scenario.BufferBatchBytes,
            ScanDuration = state.ScanStopwatch.Elapsed,
            ExecuteDuration = state.ExecuteStopwatch.Elapsed,
            CopiedFiles = state.Results.Sum(r => r.NumberOfFilesAffected),
            SkippedFiles = state.Results.Sum(r => r.NumberOfFilesSkipped),
            FailedFiles = state.Results.Count(r => !r.IsSuccess),
            OutputBytes = state.Results.Sum(r => r.OutputBytes),
            DestinationFreeSpaceBeforeBytes = state.FreeSpaceBefore,
            DestinationFreeSpaceAfterBytes = state.FreeSpaceAfter,
            JournalPath = state.JournalPath is null ? string.Empty : Path.GetFullPath(state.JournalPath),
            ExceptionType = ex?.GetType().FullName,
            ExceptionMessage = ex?.Message,
        };
    }
}

internal static class BenchmarkRunStatus
{
    public const string InProgress = "inProgress";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

internal sealed class BenchmarkFileCopyRecord
{
    public required DateTime RunStartedUtc { get; init; }
    public required int RunIndex { get; init; }
    public required string ScenarioName { get; init; }
    public required string VariantName { get; init; }
    public required string SourceRelativePath { get; init; }
    public required string DestinationPath { get; init; }
    public required long FileSizeBytes { get; init; }
    public required double CopyDurationMilliseconds { get; init; }
    public double? ThroughputMiBPerSecond { get; init; }
}

internal sealed class DatasetPreparationConfig
{
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public bool OrganizeByBucket { get; set; }
    public List<DatasetPreparationBucketConfig> Buckets { get; set; } = [];

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            throw new InvalidOperationException("datasetPreparation.sourcePath is required.");
        }

        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            throw new InvalidOperationException("datasetPreparation.destinationPath is required.");
        }

        SourcePath = Path.GetFullPath(SourcePath);
        DestinationPath = Path.GetFullPath(DestinationPath);

        if (Buckets.Count == 0)
        {
            throw new InvalidOperationException("datasetPreparation.buckets must contain at least one bucket.");
        }

        foreach (var bucket in Buckets)
        {
            bucket.Normalize();
        }

        var duplicateName = Buckets
            .GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateName is not null)
        {
            throw new InvalidOperationException($"datasetPreparation bucket name '{duplicateName.Key}' is duplicated.");
        }

        var orderedBuckets = Buckets.OrderBy(b => b.MinimumFileSizeBytes).ToList();
        for (var i = 1; i < orderedBuckets.Count; i++)
        {
            if (orderedBuckets[i].MinimumFileSizeBytes <= orderedBuckets[i - 1].MaximumFileSizeBytes)
            {
                throw new InvalidOperationException(
                    $"datasetPreparation bucket '{orderedBuckets[i].Name}' overlaps '{orderedBuckets[i - 1].Name}'.");
            }
        }
    }

    public DatasetPreparationBucketConfig? FindBucket(long sizeBytes) =>
        Buckets.FirstOrDefault(b => b.Contains(sizeBytes));

}

internal sealed class DatasetPreparationBucketConfig
{
    public string Name { get; set; } = string.Empty;
    public long MinimumFileSizeBytes { get; set; }
    public long MaximumFileSizeBytes { get; set; }
    public long TargetTotalBytes { get; set; }

    public void Normalize()
    {
        Name = Name.Trim();
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("datasetPreparation bucket name is required.");
        }

        if (MinimumFileSizeBytes < 0)
        {
            throw new InvalidOperationException($"datasetPreparation bucket '{Name}' has a negative minimum size.");
        }

        if (MaximumFileSizeBytes < MinimumFileSizeBytes)
        {
            throw new InvalidOperationException($"datasetPreparation bucket '{Name}' has max size below min size.");
        }

        if (TargetTotalBytes <= 0)
        {
            throw new InvalidOperationException($"datasetPreparation bucket '{Name}' targetTotalBytes must be positive.");
        }
    }

    public bool Contains(long sizeBytes) =>
        sizeBytes >= MinimumFileSizeBytes && sizeBytes <= MaximumFileSizeBytes;

    public bool Matches(DatasetPreparationBucketConfig other) =>
        string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) &&
        MinimumFileSizeBytes == other.MinimumFileSizeBytes &&
        MaximumFileSizeBytes == other.MaximumFileSizeBytes &&
        TargetTotalBytes == other.TargetTotalBytes;
}

internal sealed class DatasetPreparationBucketState
{
    public string BucketName { get; set; } = string.Empty;
    public int ActualFileCount { get; set; }
    public long ActualTotalBytes { get; set; }
}

internal sealed class DatasetPreparationImportedFileRecord
{
    public required string SourcePath { get; init; }
    public required string RelativePath { get; init; }
    public required string DestinationPath { get; init; }
    public required string BucketName { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTime ImportedUtc { get; init; }
}

internal sealed class DatasetPreparationRunSummary
{
    public required DateTime RunStartedUtc { get; init; }
    public required DateTime RunCompletedUtc { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required string SummaryPath { get; init; }
    public string? Notes { get; init; }
    public required int ImportedFileCount { get; init; }
    public required long ImportedTotalBytes { get; init; }
    public required int DuplicateSourceSkips { get; init; }
    public required int ExistingDestinationSkips { get; init; }
    public required List<DatasetPreparationImportedFileRecord> ImportedFiles { get; init; }
    public required List<DatasetPreparationBucketProgress> Buckets { get; init; }
}

internal sealed class DatasetPreparationBucketProgress
{
    public required string BucketName { get; init; }
    public required long TargetTotalBytes { get; init; }
    public required int BeforeFileCount { get; init; }
    public required long BeforeTotalBytes { get; init; }
    public required int AddedFileCount { get; init; }
    public required long AddedTotalBytes { get; init; }
    public required int AfterFileCount { get; init; }
    public required long AfterTotalBytes { get; init; }
    public required bool IsFull { get; init; }
}

internal static class JsonOptions
{
    public static JsonSerializerOptions Default { get; } = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static JsonSerializerOptions Indented { get; } = new(Default)
    {
        WriteIndented = true,
    };
}

internal static class BenchmarkJson
{
    public static async Task<T?> ReadAsync<T>(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions.Default, ct);
    }

    public static async Task WriteAsync<T>(string path, T value, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, value, JsonOptions.Indented, ct);
    }
}

internal sealed record BenchmarkSelection(
    BenchmarkScenario Scenario,
    BenchmarkVariant Variant,
    int SuccessfulRunCount,
    int TotalRunCount,
    DateTime LastRunUtc,
    int NextRunIndex);

internal sealed record FileSizeBucket(long MinBytesInclusive, long MaxBytesInclusive, string Label)
{
    public bool Contains(long value) => value >= MinBytesInclusive && value <= MaxBytesInclusive;
}

internal sealed record RunVariantEvidence(
    string VariantName,
    int TotalRuns,
    int ValidRuns,
    int InvalidRuns,
    double MedianSeconds,
    double MeanSeconds,
    double MinSeconds,
    double MaxSeconds,
    double SpreadSeconds);

internal sealed record BucketVariantEvidence(
    string BucketLabel,
    string VariantName,
    int RecordCount,
    long TotalBytes,
    double MeanDurationMilliseconds,
    double MedianDurationMilliseconds,
    double P95DurationMilliseconds,
    double AggregateThroughputMiBPerSecond,
    double MeanThroughputMiBPerSecond,
    double P50ThroughputMiBPerSecond,
    double P95ThroughputMiBPerSecond,
    double RunMedianSpreadMilliseconds)
{
    public static BucketVariantEvidence Empty(string bucketLabel, string variantName) =>
        new(bucketLabel, variantName, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

internal sealed record EvidenceComparison(
    string Verdict,
    string DeltaText,
    string NoiseText);

internal static class FileSizeBuckets
{
    public static IReadOnlyList<FileSizeBucket> All { get; } =
    [
        new FileSizeBucket(0, 4 * 1024, "Sub4KiB"),
        new FileSizeBucket(4 * 1024 + 1, 16 * 1024, "Sub16KiB"),
        new FileSizeBucket(16 * 1024 + 1, 64 * 1024, "Sub64KiB"),
        new FileSizeBucket(64 * 1024 + 1, 256 * 1024, "Sub256KiB"),
        new FileSizeBucket(256 * 1024 + 1, 512 * 1024, "Sub512KiB"),
        new FileSizeBucket(512 * 1024 + 1, 1024 * 1024, "Sub1MiB"),
        new FileSizeBucket(1024 * 1024 + 1, 4L * 1024 * 1024, "Sub4MiB"),
        new FileSizeBucket(4L * 1024 * 1024 + 1, long.MaxValue, "Tail"),
    ];
}

internal static class FileNamesResolver
{
    public const string DefaultResults = "benchmark-results.ndjson";
    public const string DefaultFileResults = "benchmark-file-results.ndjson";
    public const string DefaultAnalysis = "benchmark-analysis.md";
    public const string DefaultSizeScaling = "benchmark-size-scaling.md";
    public const string DefaultTaskList = "benchmark-tasklist.md";

    public static (string Results, string FileResults, string Analysis, string SizeScaling, string TaskList) GetFileNames(string configPath)
    {
        var configFileName = Path.GetFileName(configPath);
        var prefix = configFileName.EndsWith(".json") ? configFileName[..^5] : configFileName;

        var results = prefix.Replace("scenarios", "results") + ".ndjson";
        if (results == prefix + ".ndjson") results = DefaultResults;

        var fileResults = prefix.Replace("scenarios", "file-results") + ".ndjson";
        if (fileResults == prefix + ".ndjson") fileResults = DefaultFileResults;

        var analysis = prefix.Replace("scenarios", "analysis") + ".md";
        if (analysis == prefix + ".md") analysis = DefaultAnalysis;

        var sizeScaling = prefix.Replace("scenarios", "size-scaling") + ".md";
        if (sizeScaling == prefix + ".md") sizeScaling = DefaultSizeScaling;

        var taskList = prefix.Replace("scenarios", "tasklist") + ".md";
        if (taskList == prefix + ".md") taskList = DefaultTaskList;

        return (results, fileResults, analysis, sizeScaling, taskList);
    }
}

internal sealed class VariantNameComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        if (x == y) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        var matchX = System.Text.RegularExpressions.Regex.Match(x, @"\d+");
        var matchY = System.Text.RegularExpressions.Regex.Match(y, @"\d+");
        
        long numX = matchX.Success ? long.Parse(matchX.Value) : -1;
        long numY = matchY.Success ? long.Parse(matchY.Value) : -1;

        // If both have numbers and the numbers differ, sort by the number
        if (numX != -1 && numY != -1 && numX != numY)
        {
            return numX.CompareTo(numY);
        }

        // Fallback to string comparison
        return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }
}

