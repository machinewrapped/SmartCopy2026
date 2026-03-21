using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
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
var resultsPath = Path.Combine(workingDirectory, ResultsFileName);
var taskListPath = Path.Combine(workingDirectory, TaskListFileName);
var journalDirectory = Path.Combine(workingDirectory, JournalDirectoryName);

if (!File.Exists(configPath))
{
    var template = BenchmarkConfig.CreateTemplate();
    await WriteJsonAsync(configPath, template, ct);
    await UpdateTaskListAsync(taskListPath, template, [], ct);
    Console.WriteLine($"Created {ConfigFileName} in {workingDirectory}.");
    Console.WriteLine("Edit the scenario file if needed, then run the benchmark app again.");
    return;
}

var config = await ReadJsonAsync<BenchmarkConfig>(configPath, ct)
    ?? throw new InvalidOperationException($"Could not read {configPath}.");
config.Normalize();

var historicalRuns = await ReadExistingRunsAsync(resultsPath, ct);
await UpdateTaskListAsync(taskListPath, config, historicalRuns, ct);

var selection = BenchmarkCliOptions.Parse(args);
var scenario = SelectScenario(config, historicalRuns, selection.ScenarioName);
if (scenario is null)
{
    Console.WriteLine("No eligible benchmark scenario found.");
    Console.WriteLine($"See {ConfigFileName} and {TaskListFileName} in {workingDirectory}.");
    return;
}

var runStartedUtc = DateTime.UtcNow;
var sourcePath = Path.GetFullPath(config.SourcePath);
var destinationPath = Path.GetFullPath(scenario.DestinationPath);

ValidatePaths(sourcePath, destinationPath);

Console.WriteLine($"Scenario: {scenario.Name}");
Console.WriteLine($"Source:   {sourcePath}");
Console.WriteLine($"Target:   {destinationPath}");

var state = new BenchmarkState();

try
{
    if (scenario.ClearDestinationBeforeRun)
    {
        Console.WriteLine("Clearing destination contents before benchmark.");
        ClearDirectoryContents(destinationPath);
    }

    Directory.CreateDirectory(Path.GetDirectoryName(taskListPath) ?? workingDirectory);
    Directory.CreateDirectory(journalDirectory);

    var sourceProvider = new LocalFileSystemProvider(sourcePath);
    var registry = new FileSystemProviderRegistry();
    registry.Register(sourceProvider);

    var scanOptions = new ScanOptions
    {
        IncludeHidden = config.IncludeHidden,
        FullPreScan = true,
        LazyExpand = false,
        FollowSymlinks = false,
    };

    state.ScanStopwatch.Start();
    state.Root = await ScanTreeAsync(sourceProvider, sourcePath, scanOptions, ct);
    state.ScanStopwatch.Stop();

    new SelectionManager().SelectAll(state.Root);
    state.Root.BuildStats();

    var previewRunner = new PipelineRunner(new TransformPipeline([new CopyStep(destinationPath, scenario.OverwriteMode)]));
    state.PreviewStopwatch.Start();
    state.Preview = await previewRunner.PreviewAsync(new PipelineJob
    {
        RootNode = state.Root,
        SourceProvider = sourceProvider,
        ProviderRegistry = registry,
        CancellationToken = ct,
    }, ct);
    state.PreviewStopwatch.Stop();

    var destinationProvider = registry.ResolveProvider(destinationPath)
        ?? throw new InvalidOperationException($"No destination provider for {destinationPath}.");

    state.FreeSpaceBefore = await destinationProvider.GetAvailableFreeSpaceAsync(ct);

    var runner = new PipelineRunner(new TransformPipeline([new CopyStep(destinationPath, scenario.OverwriteMode)]));
    state.ExecuteStopwatch.Start();
    state.Results = await runner.ExecuteAsync(new PipelineJob
    {
        RootNode = state.Root,
        SourceProvider = sourceProvider,
        ProviderRegistry = registry,
        CancellationToken = ct,
    }, ct);
    state.ExecuteStopwatch.Stop();

    state.FreeSpaceAfter = await destinationProvider.GetAvailableFreeSpaceAsync(ct);

    var journal = new OperationJournal(journalDirectory);
    state.JournalPath = await journal.WriteAsync(
        state.Results.Where(r => r.SourceNodeResult != SourceResult.None),
        ct);

    var record = BenchmarkRunRecord.CreateSuccess(
        scenario,
        sourcePath,
        destinationPath,
        runStartedUtc,
        state,
        selection.Notes);

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
        sourcePath,
        destinationPath,
        runStartedUtc,
        state,
        selection.Notes,
        ex);

    await AppendJsonLineAsync(resultsPath, record, ct);

    historicalRuns.Add(record);
    await UpdateTaskListAsync(taskListPath, config, historicalRuns, ct);
    throw;
}

static async Task<DirectoryNode> ScanTreeAsync(
    IFileSystemProvider sourceProvider,
    string sourcePath,
    ScanOptions scanOptions,
    CancellationToken ct)
{
    var scanner = new DirectoryScanner(sourceProvider);
    DirectoryNode? root = null;

    await foreach (var node in scanner.ScanAsync(sourcePath, scanOptions, progress: null, ct))
    {
        root ??= node as DirectoryNode;
    }

    return root ?? throw new InvalidOperationException($"Scan did not produce a directory root for {sourcePath}.");
}

static BenchmarkScenario? SelectScenario(
    BenchmarkConfig config,
    IReadOnlyList<BenchmarkRunRecord> historicalRuns,
    string? explicitScenarioName)
{
    if (!string.IsNullOrWhiteSpace(explicitScenarioName))
    {
        return config.Scenarios.FirstOrDefault(s =>
            s.Enabled &&
            string.Equals(s.Name, explicitScenarioName, StringComparison.OrdinalIgnoreCase));
    }

    return config.Scenarios
        .Where(s => s.Enabled)
        .OrderBy(s => historicalRuns.Count(r => IsSuccessfulRunForScenario(r, s.Name)))
        .ThenBy(s => historicalRuns
            .Where(r => string.Equals(r.ScenarioName, s.Name, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.RunStartedUtc)
            .DefaultIfEmpty(DateTime.MinValue)
            .Max())
        .FirstOrDefault();
}

static bool IsSuccessfulRunForScenario(BenchmarkRunRecord run, string scenarioName) =>
    string.Equals(run.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase) &&
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
    if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
    {
        return path;
    }

    return path + Path.DirectorySeparatorChar;
}

static void ClearDirectoryContents(string destinationPath)
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
        File.Delete(file);
    }

    foreach (var directory in Directory.EnumerateDirectories(fullDestinationPath))
    {
        Directory.Delete(directory, recursive: true);
    }
}

static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken ct)
{
    await using var stream = File.OpenRead(path);
    return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions.Default, ct);
}

static async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
{
    await using var stream = File.Create(path);
    await JsonSerializer.SerializeAsync(stream, value, JsonOptions.Indented, ct);
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
    builder.AppendLine("| Scenario | Destination | Enabled | Successful runs | Last run (UTC) | Last status |");
    builder.AppendLine("|---|---|---|---:|---|---|");

    foreach (var scenario in config.Scenarios)
    {
        var scenarioRuns = runs
            .Where(r => string.Equals(r.ScenarioName, scenario.Name, StringComparison.OrdinalIgnoreCase))
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
            .Append(EscapeTable(Path.GetFullPath(scenario.DestinationPath))).Append(" | ")
            .Append(scenario.Enabled ? "yes" : "no").Append(" | ")
            .Append(successfulRuns).Append(" | ")
            .Append(lastRun?.RunStartedUtc.ToString("O") ?? "-").Append(" | ")
            .Append(EscapeTable(lastStatus)).AppendLine(" |");
    }

    await File.WriteAllTextAsync(taskListPath, builder.ToString(), ct);
}

static string EscapeTable(string value) => value.Replace("|", "\\|");

file sealed class BenchmarkCliOptions
{
    public string? ScenarioName { get; init; }
    public string? Notes { get; init; }

    public static BenchmarkCliOptions Parse(string[] args)
    {
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
        }

        return new BenchmarkCliOptions
        {
            ScenarioName = scenarioName,
            Notes = notes,
        };
    }
}

file sealed class BenchmarkConfig
{
    public string SourcePath { get; set; } = @"R:\TestData\MP3";
    public bool IncludeHidden { get; set; }
    public List<BenchmarkScenario> Scenarios { get; set; } = [];

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
        };

    public void Normalize()
    {
        SourcePath = Path.GetFullPath(SourcePath);
        foreach (var scenario in Scenarios)
        {
            scenario.Name = scenario.Name.Trim();
            scenario.DestinationPath = Path.GetFullPath(scenario.DestinationPath);
        }
    }
}

file sealed class BenchmarkScenario
{
    public string Name { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public bool ClearDestinationBeforeRun { get; set; } = true;
    public OverwriteMode OverwriteMode { get; set; } = OverwriteMode.Always;
}

file sealed class BenchmarkState
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

file sealed class BenchmarkRunRecord
{
    public required string ScenarioName { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required DateTime RunStartedUtc { get; init; }
    public required string HostName { get; init; }
    public required string OsDescription { get; init; }
    public required string FrameworkDescription { get; init; }
    public required string? Notes { get; init; }
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
        DateTime runStartedUtc,
        BenchmarkState state,
        string? notes) => Create(scenario, sourcePath, destinationPath, runStartedUtc, state, notes, ex: null);

    public static BenchmarkRunRecord CreateFailure(
        BenchmarkScenario scenario,
        string sourcePath,
        string destinationPath,
        DateTime runStartedUtc,
        BenchmarkState state,
        string? notes,
        Exception ex) => Create(scenario, sourcePath, destinationPath, runStartedUtc, state, notes, ex);

    private static BenchmarkRunRecord Create(
        BenchmarkScenario scenario,
        string sourcePath,
        string destinationPath,
        DateTime runStartedUtc,
        BenchmarkState state,
        string? notes,
        Exception? ex)
    {
        return new BenchmarkRunRecord
        {
            ScenarioName = scenario.Name,
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            RunStartedUtc = runStartedUtc,
            HostName = Environment.MachineName,
            OsDescription = RuntimeInformation.OSDescription,
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            Notes = notes,
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

file static class JsonOptions
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
