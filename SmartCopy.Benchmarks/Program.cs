using SmartCopy.Benchmarks;

var ct = CancellationToken.None;
var workingDirectory = Directory.GetCurrentDirectory();
var selection = BenchmarkCliOptions.Parse(args);

if (selection.Help)
{
    Console.WriteLine("SmartCopy Benchmarks");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --help, -h, -?       Show this help message and exit");
    Console.WriteLine("  --config <path>      Required path to benchmark configuration file");
    Console.WriteLine("  --mode <mode>        Execution mode: benchmark, dataset-prep, analysis, size-scaling, validation, compare, remove-records");
    Console.WriteLine("  --compare-with <dir> Directory containing benchmark-file-results.ndjson to compare against");
    Console.WriteLine("  --scenario <name>    Filter execution to a specific scenario name");
    Console.WriteLine("  --variant <name>     Filter execution to a specific variant name");
    Console.WriteLine("  --runs <n>           Run a fixed number of rounds (explicit mode; skips convergence)");
    Console.WriteLine("  --remove, --prune    Shorthand for --mode remove-records");
    Console.WriteLine("  --mode remove-records --scenario <name> [--variant <name>]");
    Console.WriteLine("                       Remove matching run-level and file-level result records");
    Console.WriteLine("  --notes <text>       Add notes to the run");
    Console.WriteLine("  --fresh              Start fresh, ignoring any existing results");
    return 0;
}

if (string.IsNullOrWhiteSpace(selection.ConfigPath))
{
    Console.Error.WriteLine("Missing required --config <path>. Use --help to see supported options.");
    return 2;
}

var configPathArgument = selection.ConfigPath;
var configPath = Path.IsPathFullyQualified(configPathArgument)
    ? configPathArgument
    : Path.Combine(workingDirectory, configPathArgument);

if (!File.Exists(configPath))
{
    await BenchmarkJson.WriteAsync(configPath, BenchmarkConfig.CreateScaffold(), ct);
    Console.WriteLine($"Created an empty benchmark configuration scaffold: {configPath}.");
    Console.WriteLine("Set sourcePath and add one or more scenarios before running the benchmark.");
    return 0;
}

var config = await BenchmarkJson.ReadAsync<BenchmarkConfig>(configPath, ct)
    ?? throw new InvalidOperationException($"Could not read {configPath}.");
config.Normalize();

if (selection.Mode == BenchmarkRunMode.DatasetPreparation)
{
    await DatasetPreparationRunner.RunAsync(workingDirectory, config, selection, ct);
    return 0;
}

if (selection.Mode == BenchmarkRunMode.Analysis)
{
    await AnalysisRunner.RunAsync(workingDirectory, config, selection, ct);
    return 0;
}

if (selection.Mode == BenchmarkRunMode.SizeScaling)
{
    await SizeScalingRunner.RunAsync(workingDirectory, config, selection, ct);
    return 0;
}

if (selection.Mode == BenchmarkRunMode.Validation)
{
    await ValidationModeRunner.RunAsync(workingDirectory, config, selection, ct);
    await ArchiveLatestRunAsync();
    return 0;
}

if (selection.Mode == BenchmarkRunMode.Compare)
{
    await CompareRunner.RunAsync(workingDirectory, config, selection, ct);
    return 0;
}

if (selection.Mode == BenchmarkRunMode.RemoveRecords)
{
    await BenchmarkRecordRemovalRunner.RunAsync(workingDirectory, config, selection, ct);
    return 0;
}

await BenchmarkModeRunner.RunAsync(workingDirectory, config, selection, ct);
await ArchiveLatestRunAsync();
return 0;

async Task ArchiveLatestRunAsync()
{
    var artifactDirectory = BenchmarkHelpers.ResolveArtifactDirectory(workingDirectory, config.SourcePath, config.ArtifactPath);
    await BenchmarkModeRunner.ArchiveResultsAsync(artifactDirectory, configPathArgument, ct);
}
