using SmartCopy.Benchmarks;

var ct = CancellationToken.None;
var workingDirectory = Directory.GetCurrentDirectory();
var selection = BenchmarkCliOptions.Parse(args);
var configPath = Path.IsPathFullyQualified(selection.ConfigPath)
    ? selection.ConfigPath
    : Path.Combine(workingDirectory, selection.ConfigPath);

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

if (selection.Mode == BenchmarkRunMode.Converge)
{
    await ConvergenceModeRunner.RunAsync(workingDirectory, config, selection, ct);
    return 0;
}

await BenchmarkModeRunner.RunAsync(workingDirectory, config, selection, ct);

return 0;
