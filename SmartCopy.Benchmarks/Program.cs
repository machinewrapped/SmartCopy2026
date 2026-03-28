using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SmartCopy.Benchmarks;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Progress;
using SmartCopy.Core.Scanning;
using SmartCopy.Core.Selection;

const string ConfigFileName = "benchmark-scenarios.json";
const string ResultsFileName = "benchmark-results.ndjson";
const string TaskListFileName = "benchmark-tasklist.md";
const string JournalDirectoryName = "benchmark-journals";

var ct = CancellationToken.None;
var workingDirectory = Directory.GetCurrentDirectory();
var configPath = Path.Combine(workingDirectory, ConfigFileName);
var selection = BenchmarkCliOptions.Parse(args);

if (!File.Exists(configPath))
{
    var template = BenchmarkConfig.CreateTemplate();
    await BenchmarkJson.WriteAsync(configPath, template, ct);
    Console.WriteLine($"Created {ConfigFileName} in {workingDirectory}.");
    Console.WriteLine("Edit the scenario file if needed, then run the benchmark app again.");
    return 0;
}

var config = await BenchmarkJson.ReadAsync<BenchmarkConfig>(configPath, ct)
    ?? throw new InvalidOperationException($"Could not read {configPath}.");
config.Normalize();

if (selection.Mode == BenchmarkRunMode.DatasetPreparation)
{
    await RunDatasetPreparationModeAsync(workingDirectory, config, selection, ct);
    return 0;
}

await RunBenchmarkModeAsync(workingDirectory, config, selection, ct);

return 0;

static async Task RunDatasetPreparationModeAsync(
    string workingDirectory,
    BenchmarkConfig config,
    BenchmarkCliOptions selection,
    CancellationToken ct)
{
    var preparation = config.DatasetPreparation
        ?? throw new InvalidOperationException("benchmark-scenarios.json does not define datasetPreparation.");
    var artifactDirectory = ResolveArtifactDirectory(workingDirectory, preparation.SourcePath, config.ArtifactPath);

    Console.WriteLine("Mode:     dataset-prep");
    Console.WriteLine($"Source:   {preparation.SourcePath}");
    Console.WriteLine($"Dataset:  {preparation.DestinationPath}");
    Console.WriteLine($"Artifacts:{artifactDirectory}");

    var service = new DatasetPreparationService();
    DatasetPreparationRunSummary summary;

    using (var progress = new ThrottledConsoleProgress<DatasetPreparationProgress>(p =>
        UpdateProgress($"Prep: Scanned={p.TotalFilesScanned}, Imported={p.TotalFilesImported} ({FormatSize(p.TotalBytesImported)}), File={p.CurrentFile}")))
    {
        summary = await service.RunAsync(preparation, artifactDirectory, config.IncludeHidden, selection.Notes, progress, ct);
    }
    Console.WriteLine(); // Finalize progress line

    Console.WriteLine($"Imported files: {summary.ImportedFileCount}, bytes: {summary.ImportedTotalBytes}.");
    Console.WriteLine($"Skipped duplicates: {summary.DuplicateSourceSkips}, skipped conflicts: {summary.ExistingDestinationSkips}.");
    Console.WriteLine($"Summary:  {summary.SummaryPath}");

    foreach (var bucket in summary.Buckets)
    {
        var status = bucket.IsFull ? "full" : "underfilled";
        Console.WriteLine(
            $"Bucket {bucket.BucketName}: +{bucket.AddedFileCount} files / +{bucket.AddedTotalBytes} bytes, " +
            $"now {bucket.AfterFileCount} files / {bucket.AfterTotalBytes} bytes ({status}).");
    }
}

static async Task RunBenchmarkModeAsync(
    string workingDirectory,
    BenchmarkConfig config,
    BenchmarkCliOptions selection,
    CancellationToken ct)
{
    var artifactDirectory = ResolveArtifactDirectory(workingDirectory, config.SourcePath, config.ArtifactPath);
    var resultsPath = Path.Combine(artifactDirectory, ResultsFileName);
    var taskListPath = Path.Combine(artifactDirectory, TaskListFileName);
    var journalDirectory = Path.Combine(artifactDirectory, JournalDirectoryName);

    Directory.CreateDirectory(artifactDirectory);

    var historicalRuns = await ReadExistingRunsAsync(resultsPath, ct);
    await UpdateTaskListAsync(taskListPath, config, historicalRuns, ct);

    var benchmarkSelection = SelectBenchmarkSelection(config, historicalRuns, selection);
    if (benchmarkSelection is null)
    {
        Console.WriteLine("No eligible benchmark scenario found.");
        Console.WriteLine($"See {ConfigFileName} and {TaskListFileName} in {workingDirectory}.");
        return;
    }

    var scenario = benchmarkSelection.Scenario;
    var variant = benchmarkSelection.Variant;
    var nextRunIndex = benchmarkSelection.NextRunIndex;

    var runStartedUtc = DateTime.UtcNow;
    var sourcePath = Path.GetFullPath(config.SourcePath);
    var destinationPath = Path.GetFullPath(scenario.DestinationPath);

    ValidatePaths(sourcePath, destinationPath);

    var providerOptions = variant.CreateProviderOptions(scenario);
    var overwriteMode = variant.OverwriteMode ?? scenario.OverwriteMode;

    Console.WriteLine("Mode:     benchmark");
    Console.WriteLine($"Scenario: {scenario.Name}");
    Console.WriteLine($"Variant:  {variant.Name} (run {nextRunIndex}/{variant.DesiredRunCount})");
    Console.WriteLine($"Source:   {sourcePath}");
    Console.WriteLine($"Target:   {destinationPath}");
    Console.WriteLine($"Artifacts:{artifactDirectory}");
    Console.WriteLine(
        $"Provider: buffer={providerOptions.CopyBufferSizeBytes} bytes, " +
        $"small-file-threshold={providerOptions.SmallFileProgressThresholdBytes} bytes, " +
        $"write-mode={providerOptions.WriteMode}, " +
        $"array-pool={providerOptions.UseArrayPoolForManualLoop}, " +
        $"preallocate={providerOptions.PreallocateDestinationFile}");

    var state = new BenchmarkState();

    try
    {
        if (scenario.ClearDestinationBeforeRun)
        {
            Console.WriteLine("Clearing destination contents before benchmark...");
            using var clearProgress = new ThrottledConsoleProgress<string>(s => UpdateProgress($"Clearing: {s}"));
            ClearDirectoryContents(destinationPath, clearProgress);
            Console.WriteLine(); // Finalize progress line
        }

        Directory.CreateDirectory(journalDirectory);

        var sourceProvider = new LocalFileSystemProvider(sourcePath, options: providerOptions);
        var destinationProvider = new LocalFileSystemProvider(destinationPath, options: providerOptions);
        var registry = new FileSystemProviderRegistry();
        registry.Register(sourceProvider);
        registry.Register(destinationProvider);

        var scanOptions = new ScanOptions
        {
            IncludeHidden = config.IncludeHidden,
            FullPreScan = true,
            LazyExpand = false,
            FollowSymlinks = false,
        };

        state.ScanStopwatch.Start();
        Console.WriteLine("Scanning source...");
        using (var scanProgress = new ThrottledConsoleProgress<ScanProgress>(p => UpdateProgress($"Scanned: {p.NodesDiscovered} nodes, {p.DirectoriesScanned} dirs")))
        {
            state.Root = await ScanTreeAsync(sourceProvider, sourcePath, scanOptions, scanProgress, ct);
        }
        Console.WriteLine(); // Finalize progress line
        state.ScanStopwatch.Stop();

        new SelectionManager().SelectAll(state.Root);
        state.Root.BuildStats();

        var previewRunner = new PipelineRunner(new TransformPipeline([new CopyStep(destinationPath, overwriteMode)]));
        state.PreviewStopwatch.Start();
        Console.Write("Previewing operations... ");
        state.Preview = await previewRunner.PreviewAsync(new PipelineJob
        {
            RootNode = state.Root,
            SourceProvider = sourceProvider,
            ProviderRegistry = registry,
            CancellationToken = ct,
        }, ct);
        Console.WriteLine("Done.");
        state.PreviewStopwatch.Stop();

        var resolvedDestinationProvider = registry.ResolveProvider(destinationPath)
            ?? throw new InvalidOperationException($"No destination provider for {destinationPath}.");

        state.FreeSpaceBefore = await resolvedDestinationProvider.GetAvailableFreeSpaceAsync(ct);

        var runner = new PipelineRunner(new TransformPipeline([new CopyStep(destinationPath, overwriteMode)]));
        state.ExecuteStopwatch.Start();
        Console.WriteLine("Executing copy...");
        using (var executeProgress = new ThrottledConsoleProgress<OperationProgress>(p =>
            UpdateProgress($"Copying: {p.FilesCompleted}/{p.FilesTotal} files ({FormatSize(p.TotalBytesCompleted)}/{FormatSize(p.TotalBytes)}), ETR: {FormatDuration(p.EstimatedRemaining)}")))
        {
            state.ExecuteStopwatch.Start();
            state.Results = await runner.ExecuteAsync(new PipelineJob
            {
                RootNode = state.Root,
                SourceProvider = sourceProvider,
                ProviderRegistry = registry,
                Progress = executeProgress,
                CancellationToken = ct,
            }, ct);
            state.ExecuteStopwatch.Stop();
        }
        Console.WriteLine(); // Finalize progress line

        state.FreeSpaceAfter = await resolvedDestinationProvider.GetAvailableFreeSpaceAsync(ct);

        var journal = new OperationJournal(journalDirectory);
        state.JournalPath = await journal.WriteAsync(
            state.Results.Where(r => r.SourceNodeResult != SourceResult.None),
            ct);

        var record = BenchmarkRunRecord.CreateSuccess(
            scenario,
            variant,
            sourcePath,
            destinationPath,
            artifactDirectory,
            runStartedUtc,
            state,
            selection.Notes,
            nextRunIndex);

        await AppendJsonLineAsync(resultsPath, record, ct);

        historicalRuns.Add(record);
        await UpdateTaskListAsync(taskListPath, config, historicalRuns, ct);

        Console.WriteLine($"Completed in {record.ExecuteDuration}.");
        Console.WriteLine($"Copied files: {record.CopiedFiles}, failed: {record.FailedFiles}, skipped: {record.SkippedFiles}.");
        Console.WriteLine($"Results: {resultsPath}");
        Console.WriteLine($"Journal: {state.JournalPath}");
    }
    catch (Exception ex)
    {
        var record = BenchmarkRunRecord.CreateFailure(
            scenario,
            variant,
            sourcePath,
            destinationPath,
            artifactDirectory,
            runStartedUtc,
            state,
            selection.Notes,
            nextRunIndex,
            ex);

        await AppendJsonLineAsync(resultsPath, record, ct);

        historicalRuns.Add(record);
        await UpdateTaskListAsync(taskListPath, config, historicalRuns, ct);
        throw;
    }
}

static async Task<DirectoryNode> ScanTreeAsync(
    IFileSystemProvider sourceProvider,
    string sourcePath,
    ScanOptions scanOptions,
    IProgress<ScanProgress>? progress,
    CancellationToken ct)
{
    var scanner = new DirectoryScanner(sourceProvider);
    DirectoryNode? root = null;

    await foreach (var node in scanner.ScanAsync(sourcePath, scanOptions, progress, ct))
    {
        root ??= node as DirectoryNode;
    }

    return root ?? throw new InvalidOperationException($"Scan did not produce a directory root for {sourcePath}.");
}

static BenchmarkSelection? SelectBenchmarkSelection(
    BenchmarkConfig config,
    IReadOnlyList<BenchmarkRunRecord> historicalRuns,
    BenchmarkCliOptions selection)
{
    var candidates = (
        from scenario in config.Scenarios
        where scenario.Enabled
        from variant in config.Variants
        where variant.Enabled
        let successfulRuns = historicalRuns.Count(r => IsSuccessfulRunForScenarioVariant(r, scenario.Name, variant.Name))
        let totalRuns = historicalRuns.Count(r => IsRunForScenarioVariant(r, scenario.Name, variant.Name))
        let lastRunUtc = historicalRuns
            .Where(r => IsRunForScenarioVariant(r, scenario.Name, variant.Name))
            .Select(r => r.RunStartedUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max()
        let nextRunIndex = totalRuns + 1
        select new BenchmarkSelection(scenario, variant, successfulRuns, totalRuns, lastRunUtc, nextRunIndex))
        .ToList();

    if (!string.IsNullOrWhiteSpace(selection.ScenarioName) || !string.IsNullOrWhiteSpace(selection.VariantName))
    {
        candidates = candidates
            .Where(c =>
                (string.IsNullOrWhiteSpace(selection.ScenarioName) ||
                 string.Equals(c.Scenario.Name, selection.ScenarioName, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(selection.VariantName) ||
                 string.Equals(c.Variant.Name, selection.VariantName, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    return candidates
        .OrderBy(c => c.SuccessfulRunCount >= c.Variant.DesiredRunCount ? 1 : 0)
        .ThenBy(c => c.SuccessfulRunCount)
        .ThenBy(c => c.LastRunUtc)
        .FirstOrDefault();
}

static bool IsRunForScenarioVariant(BenchmarkRunRecord run, string scenarioName, string variantName) =>
    string.Equals(run.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase) &&
    string.Equals(run.VariantName, variantName, StringComparison.OrdinalIgnoreCase);

static bool IsSuccessfulRunForScenarioVariant(BenchmarkRunRecord run, string scenarioName, string variantName) =>
    IsRunForScenarioVariant(run, scenarioName, variantName) &&
    run.FailedFiles == 0 &&
    run.ExceptionType is null;

static async Task<List<BenchmarkRunRecord>> ReadExistingRunsAsync(string resultsPath, CancellationToken ct)
{
    var runs = new List<BenchmarkRunRecord>();
    if (!File.Exists(resultsPath))
    {
        return runs;
    }

    await foreach (var line in File.ReadLinesAsync(resultsPath, ct))
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        try
        {
            var run = JsonSerializer.Deserialize<BenchmarkRunRecord>(line, JsonOptions.Default);
            if (run is not null)
            {
                runs.Add(run);
            }
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Warning: Skipping malformed line in results file: {ex.Message}");
        }
    }

    return runs;
}

static void ValidatePaths(string sourcePath, string destinationPath)
{
    if (!Directory.Exists(sourcePath))
    {
        throw new DirectoryNotFoundException(sourcePath);
    }

    if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Source and destination paths must differ.");
    }

    if (IsSameOrNestedPath(sourcePath, destinationPath) || IsSameOrNestedPath(destinationPath, sourcePath))
    {
        throw new InvalidOperationException("Source and destination paths must not be nested inside each other.");
    }
}

static bool IsSameOrNestedPath(string parentPath, string childPath)
{
    var normalizedParent = EnsureTrailingSeparator(Path.GetFullPath(parentPath));
    var normalizedChild = EnsureTrailingSeparator(Path.GetFullPath(childPath));
    return normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
}

static string EnsureTrailingSeparator(string path)
{
    if (path.Length > 0 &&
        (path[^1] == Path.DirectorySeparatorChar || path[^1] == Path.AltDirectorySeparatorChar))
    {
        return path;
    }

    return path + Path.DirectorySeparatorChar;
}

static string ResolveArtifactDirectory(string workingDirectory, string sourcePath, string? configuredArtifactPath)
{
    if (!string.IsNullOrWhiteSpace(configuredArtifactPath))
    {
        return Path.GetFullPath(configuredArtifactPath, workingDirectory);
    }

    var fullWorkingDirectory = Path.GetFullPath(workingDirectory);
    var fullSourcePath = Path.GetFullPath(sourcePath);
    if (IsSameOrNestedPath(fullSourcePath, fullWorkingDirectory) || IsSameOrNestedPath(fullWorkingDirectory, fullSourcePath))
    {
        var sourceParent = Path.GetDirectoryName(fullSourcePath) ?? fullWorkingDirectory;
        var sourceLeaf = Path.GetFileName(fullSourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Combine(sourceParent, $"{sourceLeaf}-benchmark-artifacts");
    }

    return fullWorkingDirectory;
}

static void ClearDirectoryContents(string destinationPath, IProgress<string>? progress = null)
{
    var fullDestinationPath = Path.GetFullPath(destinationPath);
    if (Path.GetPathRoot(fullDestinationPath)?.Equals(fullDestinationPath, StringComparison.OrdinalIgnoreCase) == true)
    {
        throw new InvalidOperationException($"Refusing to clear drive root: {fullDestinationPath}");
    }

    if (!Directory.Exists(fullDestinationPath))
    {
        Directory.CreateDirectory(fullDestinationPath);
        return;
    }

    foreach (var file in Directory.EnumerateFiles(fullDestinationPath))
    {
        progress?.Report(Path.GetFileName(file));
        File.Delete(file);
    }

    foreach (var directory in Directory.EnumerateDirectories(fullDestinationPath))
    {
        progress?.Report(Path.GetFileName(directory));
        Directory.Delete(directory, recursive: true);
    }
}

static async Task AppendJsonLineAsync<T>(string path, T value, CancellationToken ct)
{
    var line = JsonSerializer.Serialize(value, JsonOptions.Default);
    await File.AppendAllTextAsync(path, line + Environment.NewLine, Encoding.UTF8, ct);
}

static async Task UpdateTaskListAsync(
    string taskListPath,
    BenchmarkConfig config,
    IReadOnlyList<BenchmarkRunRecord> runs,
    CancellationToken ct)
{
    var builder = new StringBuilder();
    builder.AppendLine("# Benchmark Task List");
    builder.AppendLine();
    builder.AppendLine($"Source: `{Path.GetFullPath(config.SourcePath)}`");
    builder.AppendLine();
    builder.AppendLine("| Scenario | Variant | Destination | Target runs | Successful runs | Last run (UTC) | Last status |");
    builder.AppendLine("|---|---|---|---:|---:|---|---|");

    foreach (var variant in config.Variants.Where(v => v.Enabled))
    {
        foreach (var scenario in config.Scenarios.Where(s => s.Enabled))
        {
            var scenarioRuns = runs
                .Where(r => IsRunForScenarioVariant(r, scenario.Name, variant.Name))
                .OrderByDescending(r => r.RunStartedUtc)
                .ToList();
            var lastRun = scenarioRuns.FirstOrDefault();
            var successfulRuns = scenarioRuns.Count(r => r.FailedFiles == 0 && r.ExceptionType is null);
            var lastStatus = lastRun is null
                ? "pending"
                : lastRun.ExceptionType is not null
                    ? $"error ({lastRun.ExceptionType})"
                    : lastRun.FailedFiles > 0
                        ? $"failed ({lastRun.FailedFiles})"
                        : "ok";

            builder.Append("| ")
                .Append(EscapeTable(scenario.Name)).Append(" | ")
                .Append(EscapeTable(variant.Name)).Append(" | ")
                .Append(EscapeTable(Path.GetFullPath(scenario.DestinationPath))).Append(" | ")
                .Append(variant.DesiredRunCount).Append(" | ")
                .Append(successfulRuns).Append(" | ")
                .Append(lastRun?.RunStartedUtc.ToString("O") ?? "-").Append(" | ")
                .Append(EscapeTable(lastStatus)).AppendLine(" |");
        }
    }

    await File.WriteAllTextAsync(taskListPath, builder.ToString(), ct);
}

static string EscapeTable(string value) => value.Replace("|", "\\|");

static void UpdateProgress(string message)
{
    // Use carriage return to overwrite the current line
    // ANSI escape code \x1b[K clears from cursor to end of line
    Console.Write($"\r{message}\x1b[K");
}

static string FormatSize(long bytes)
{
    string[] units = { "B", "KB", "MB", "GB", "TB" };
    double len = bytes;
    int order = 0;
    while (len >= 1024 && order < units.Length - 1)
    {
        order++;
        len /= 1024;
    }
    return $"{len:0.##} {units[order]}";
}

static string FormatDuration(TimeSpan duration)
{
    if (duration == TimeSpan.Zero) return "0s";
    if (duration.TotalDays >= 1) return $"{(int)duration.TotalDays}d {duration.Hours}h";
    if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours}h {duration.Minutes}m";
    if (duration.TotalMinutes >= 1) return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
    return $"{(int)duration.TotalSeconds}s";
}

internal sealed class ThrottledConsoleProgress<T> : IProgress<T>, IDisposable
{
    private readonly Action<T> _handler;
    private readonly TimeSpan _throttle;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private T? _lastValue;
    private bool _hasValue;

    public ThrottledConsoleProgress(Action<T> handler, TimeSpan? throttle = null)
    {
        _handler = handler;
        _throttle = throttle ?? TimeSpan.FromMilliseconds(100);
    }

    public void Report(T value)
    {
        _lastValue = value;
        _hasValue = true;

        if (_stopwatch.Elapsed >= _throttle)
        {
            _handler(value);
            _stopwatch.Restart();
            _hasValue = false;
        }
    }

    public void Dispose()
    {
        if (_hasValue && _lastValue != null)
        {
            _handler(_lastValue);
        }
    }
}

internal sealed record BenchmarkSelection(
    BenchmarkScenario Scenario,
    BenchmarkVariant Variant,
    int SuccessfulRunCount,
    int TotalRunCount,
    DateTime LastRunUtc,
    int NextRunIndex);
