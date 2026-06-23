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
    Validation,
    Compare,
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
    public bool? ProviderWriteSequentialScan { get; init; }
    public long? DirectWriteThresholdBytes { get; init; }
    public long? BufferBatchBytes { get; init; }
    public long? BatchEligibilityThresholdBytes { get; init; }
    public bool? DestinationRoutingEnabled { get; init; }
    public long? ProductionBatchBufferBytes { get; init; }
    public long? ProductionBatchEligibilityCeilingBytes { get; init; }
    public long? ProductionTinyFileFastPathThresholdBytes { get; init; }
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
        int runIndex,
        OperationalSettings? recordedSettings = null)
    {
        var providerOptions = recordedSettings ?? variant.CreateOperationalSettings(scenario);
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
            ProviderWriteSequentialScan = variant.ProviderWriteSequentialScan ?? scenario.ProviderWriteSequentialScan,
            DirectWriteThresholdBytes = variant.DirectWriteThresholdBytes ?? scenario.DirectWriteThresholdBytes,
            BufferBatchBytes = variant.BufferBatchBytes ?? scenario.BufferBatchBytes,
            BatchEligibilityThresholdBytes = variant.BatchEligibilityThresholdBytes ?? scenario.BatchEligibilityThresholdBytes,
            DestinationRoutingEnabled = providerOptions.DestinationRoutingEnabled,
            ProductionBatchBufferBytes = providerOptions.BatchBufferBytes,
            ProductionBatchEligibilityCeilingBytes = providerOptions.BatchEligibilityCeilingBytes,
            ProductionTinyFileFastPathThresholdBytes = providerOptions.TinyFileFastPathThresholdBytes,
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
        int runIndex,
        OperationalSettings? recordedSettings = null) => Create(scenario, variant, sourcePath, destinationPath, artifactPath, runStartedUtc, state, notes, runIndex, ex: null, recordedSettings: recordedSettings);
 
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
        Exception ex,
        OperationalSettings? recordedSettings = null) => Create(scenario, variant, sourcePath, destinationPath, artifactPath, runStartedUtc, state, notes, runIndex, ex, recordedSettings);
 
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
        Exception? ex,
        OperationalSettings? recordedSettings = null)
    {
        var providerOptions = recordedSettings ?? variant.CreateOperationalSettings(scenario);
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
            ProviderWriteSequentialScan = variant.ProviderWriteSequentialScan ?? scenario.ProviderWriteSequentialScan,
            DirectWriteThresholdBytes = variant.DirectWriteThresholdBytes ?? scenario.DirectWriteThresholdBytes,
            BufferBatchBytes = variant.BufferBatchBytes ?? scenario.BufferBatchBytes,
            BatchEligibilityThresholdBytes = variant.BatchEligibilityThresholdBytes ?? scenario.BatchEligibilityThresholdBytes,
            DestinationRoutingEnabled = providerOptions.DestinationRoutingEnabled,
            ProductionBatchBufferBytes = providerOptions.BatchBufferBytes,
            ProductionBatchEligibilityCeilingBytes = providerOptions.BatchEligibilityCeilingBytes,
            ProductionTinyFileFastPathThresholdBytes = providerOptions.TinyFileFastPathThresholdBytes,
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

internal sealed record BenchmarkScenarioGroup(
    BenchmarkScenario Scenario,
    IReadOnlyList<BenchmarkSelection> Variants);

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
    double TotalCopyDurationMilliseconds,
    double MeanDurationMilliseconds,
    double MedianDurationMilliseconds,
    double P95DurationMilliseconds,
    double AggregateThroughputMiBPerSecond,
    double RunMedianSpreadMilliseconds,
    double RunThroughputSpreadMiBPerSecond)
{
    public static BucketVariantEvidence Empty(string bucketLabel, string variantName) =>
        new(bucketLabel, variantName, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

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

internal sealed record SessionPaths(
    string ArtifactDirectory,
    string ResultsPath,
    string FileResultsPath,
    string TaskListPath,
    string JournalDirectory);



