using SmartCopy.Benchmarks;

var ct = CancellationToken.None;
var workingDirectory = Directory.GetCurrentDirectory();
var selection = BenchmarkCliOptions.Parse(args);
var configPath = Path.IsPathFullyQualified(selection.ConfigPath)
    ? selection.ConfigPath
    : Path.Combine(workingDirectory, selection.ConfigPath);

if (selection.Help)
{
    Console.WriteLine("SmartCopy Benchmarks");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --help, -h, -?       Show this help message and exit");
    Console.WriteLine("  --config <path>      Path to benchmark configuration file (default: benchmark-scenarios.json)");
    Console.WriteLine("  --mode <mode>        Execution mode: benchmark, dataset-prep, analysis, size-scaling, validation, compare");
    Console.WriteLine("  --compare-with <dir> Directory containing benchmark-file-results.ndjson to compare against");
    Console.WriteLine("  --scenario <name>    Filter execution to a specific scenario name");
    Console.WriteLine("  --variant <name>     Filter execution to a specific variant name");
    Console.WriteLine("  --notes <text>       Add notes to the run");
    Console.WriteLine("  --fresh              Start fresh, ignoring any existing results");
    return 0;
}

if (!File.Exists(configPath))
{
    var template = BenchmarkConfig.CreateTemplate();
    await BenchmarkJson.WriteAsync(configPath, template, ct);
    Console.WriteLine($"Created {Path.GetFileName(configPath)} in {Path.GetDirectoryName(configPath) ?? workingDirectory}.");
    Console.WriteLine("Edit the scenario file if needed, then run the benchmark app again.");
    return 0;
}

var config = await BenchmarkJson.ReadAsync<BenchmarkConfig>(configPath, ct)
    ?? throw new InvalidOperationException($"Could not read {configPath}.");
config.Normalize();

SystemSleepController.PreventSleep();
try
{
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
        return 0;
    }

    if (selection.Mode == BenchmarkRunMode.Compare)
    {
        await CompareRunner.RunAsync(workingDirectory, config, selection, ct);
        return 0;
    }

    await BenchmarkModeRunner.RunAsync(workingDirectory, config, selection, ct);
    return 0;
}
finally
{
    SystemSleepController.AllowSleep();
}
