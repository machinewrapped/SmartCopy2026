namespace SmartCopy.Benchmarks;

internal static class SizeScalingRunner
{
    public static async Task RunAsync(
        string workingDirectory,
        BenchmarkConfig config,
        BenchmarkCliOptions selection,
        CancellationToken ct)
    {
        var fileNames = FileNamesResolver.GetFileNames(selection.ConfigPath);
        var artifactDirectory = BenchmarkHelpers.ResolveArtifactDirectory(workingDirectory, config.SourcePath, config.ArtifactPath);
        var fileResultsPath = Path.Combine(artifactDirectory, fileNames.FileResults);
        var analysisPath = Path.Combine(artifactDirectory, fileNames.SizeScaling);

        if (!File.Exists(fileResultsPath))
        {
            Console.WriteLine($"No file-level results found: {fileResultsPath}");
            Console.WriteLine("Run benchmark mode first to produce benchmark-file-results.ndjson.");
            return;
        }

        var allRecords = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkFileCopyRecord>(fileResultsPath, ct);
        var filteredRecords = allRecords
            .Where(r => string.IsNullOrWhiteSpace(selection.ScenarioName) ||
                        string.Equals(r.ScenarioName, selection.ScenarioName.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(r => string.IsNullOrWhiteSpace(selection.VariantName) ||
                        string.Equals(r.VariantName, selection.VariantName.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(r => new SizeScalingInputRecord(
                r.ScenarioName,
                r.VariantName,
                r.SourceRelativePath,
                r.FileSizeBytes,
                r.CopyDurationMilliseconds,
                r.ThroughputMiBPerSecond))
            .ToList();

        if (filteredRecords.Count == 0)
        {
            Console.WriteLine("No file-level records found for the selected size-scaling filters.");
            return;
        }

        var report = BenchmarkSizeScalingAnalysis.Analyze(filteredRecords);
        var markdown = BenchmarkSizeScalingAnalysis.ToMarkdown(report, fileResultsPath);

        Directory.CreateDirectory(artifactDirectory);
        await File.WriteAllTextAsync(analysisPath, markdown, ct);

        Console.WriteLine(markdown);
        Console.WriteLine($"Size scaling analysis: {analysisPath}");
    }
}
