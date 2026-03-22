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
}

internal sealed class BenchmarkCliOptions
{
    public BenchmarkRunMode Mode { get; init; } = BenchmarkRunMode.Benchmark;
    public string? ScenarioName { get; init; }
    public string? Notes { get; init; }

    public static BenchmarkCliOptions Parse(string[] args)
    {
        var mode = BenchmarkRunMode.Benchmark;
        string? scenarioName = null;
        string? notes = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--scenario", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                scenarioName = args[++i];
            }
            else if (string.Equals(args[i], "--notes", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                notes = args[++i];
            }
            else if (string.Equals(args[i], "--mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                mode = ParseMode(args[++i]);
            }
        }

        return new BenchmarkCliOptions
        {
            Mode = mode,
            ScenarioName = scenarioName,
            Notes = notes,
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

        throw new InvalidOperationException($"Unknown benchmark mode '{value}'. Expected 'benchmark' or 'dataset-prep'.");
    }
}

internal sealed class BenchmarkConfig
{
    public string SourcePath { get; set; } = @"R:\TestData\MP3";
    public string? ArtifactPath { get; set; }
    public bool IncludeHidden { get; set; }
    public List<BenchmarkScenario> Scenarios { get; set; } = [];
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

        DatasetPreparation?.Normalize();
    }
}

internal sealed class BenchmarkScenario
{
    public string Name { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool ClearDestinationBeforeRun { get; set; } = true;
    public OverwriteMode OverwriteMode { get; set; } = OverwriteMode.Always;
    public int? ProviderCopyBufferSizeBytes { get; set; }
    public long? ProviderSmallFileProgressThresholdBytes { get; set; }

    public void Normalize()
    {
        Name = Name.Trim();
        DestinationPath = Path.GetFullPath(DestinationPath);
    }

    public LocalFileSystemProviderOptions CreateProviderOptions()
    {
        return new LocalFileSystemProviderOptions
        {
            CopyBufferSizeBytes = ProviderCopyBufferSizeBytes ?? LocalFileSystemProviderOptions.Default.CopyBufferSizeBytes,
            SmallFileProgressThresholdBytes = ProviderSmallFileProgressThresholdBytes
                ?? LocalFileSystemProviderOptions.Default.SmallFileProgressThresholdBytes,
        }.Normalize();
    }
}

internal sealed class BenchmarkState
{
    public DirectoryNode? Root { get; set; }
    public OperationPlan? Preview { get; set; }
    public IReadOnlyList<TransformResult> Results { get; set; } = [];
    public long? FreeSpaceBefore { get; set; }
    public long? FreeSpaceAfter { get; set; }
    public string? JournalPath { get; set; }
    public System.Diagnostics.Stopwatch ScanStopwatch { get; } = new();
    public System.Diagnostics.Stopwatch PreviewStopwatch { get; } = new();
    public System.Diagnostics.Stopwatch ExecuteStopwatch { get; } = new();
}

internal sealed class BenchmarkRunRecord
{
    public required string ScenarioName { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public string? ArtifactPath { get; init; }
    public required DateTime RunStartedUtc { get; init; }
    public required string HostName { get; init; }
    public required string OsDescription { get; init; }
    public required string FrameworkDescription { get; init; }
    public required string? Notes { get; init; }
    public int? ProviderCopyBufferSizeBytes { get; init; }
    public long? ProviderSmallFileProgressThresholdBytes { get; init; }
    public required TimeSpan ScanDuration { get; init; }
    public required TimeSpan PreviewDuration { get; init; }
    public required TimeSpan ExecuteDuration { get; init; }
    public required int SelectedFiles { get; init; }
    public required long SelectedBytes { get; init; }
    public required int PreviewWarnings { get; init; }
    public required int CopiedFiles { get; init; }
    public required int SkippedFiles { get; init; }
    public required int FailedFiles { get; init; }
    public required long OutputBytes { get; init; }
    public required long? DestinationFreeSpaceBeforeBytes { get; init; }
    public required long? DestinationFreeSpaceAfterBytes { get; init; }
    public required string JournalPath { get; init; }
    public required string? ExceptionType { get; init; }
    public required string? ExceptionMessage { get; init; }

    public static BenchmarkRunRecord CreateSuccess(
        BenchmarkScenario scenario,
        string sourcePath,
        string destinationPath,
        string artifactPath,
        DateTime runStartedUtc,
        BenchmarkState state,
        string? notes) => Create(scenario, sourcePath, destinationPath, artifactPath, runStartedUtc, state, notes, ex: null);

    public static BenchmarkRunRecord CreateFailure(
        BenchmarkScenario scenario,
        string sourcePath,
        string destinationPath,
        string artifactPath,
        DateTime runStartedUtc,
        BenchmarkState state,
        string? notes,
        Exception ex) => Create(scenario, sourcePath, destinationPath, artifactPath, runStartedUtc, state, notes, ex);

    private static BenchmarkRunRecord Create(
        BenchmarkScenario scenario,
        string sourcePath,
        string destinationPath,
        string artifactPath,
        DateTime runStartedUtc,
        BenchmarkState state,
        string? notes,
        Exception? ex)
    {
        var providerOptions = scenario.CreateProviderOptions();
        return new BenchmarkRunRecord
        {
            ScenarioName = scenario.Name,
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            ArtifactPath = artifactPath,
            RunStartedUtc = runStartedUtc,
            HostName = Environment.MachineName,
            OsDescription = RuntimeInformation.OSDescription,
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            Notes = notes,
            ProviderCopyBufferSizeBytes = providerOptions.CopyBufferSizeBytes,
            ProviderSmallFileProgressThresholdBytes = providerOptions.SmallFileProgressThresholdBytes,
            ScanDuration = state.ScanStopwatch.Elapsed,
            PreviewDuration = state.PreviewStopwatch.Elapsed,
            ExecuteDuration = state.ExecuteStopwatch.Elapsed,
            SelectedFiles = state.Root?.NumSelectedFiles ?? 0,
            SelectedBytes = state.Root?.TotalSelectedBytes ?? 0,
            PreviewWarnings = state.Preview?.Warnings.Count ?? 0,
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

internal sealed class DatasetPreparationConfig
{
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
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
