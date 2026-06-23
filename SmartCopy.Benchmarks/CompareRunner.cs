using System.Text;

namespace SmartCopy.Benchmarks;

internal static class CompareRunner
{
    public static async Task RunAsync(
        string workingDirectory,
        BenchmarkConfig config,
        BenchmarkCliOptions selection,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(selection.ComparePath))
        {
            Console.WriteLine("Error: --compare-with <dir> must be specified for compare mode.");
            return;
        }

        var fileNames = FileNamesResolver.GetFileNames(selection.ConfigPath);
        var artifactDirectory = BenchmarkHelpers.ResolveArtifactDirectory(workingDirectory, config.SourcePath, config.ArtifactPath);
        
        var currentFileResultsPath = Path.Combine(artifactDirectory, fileNames.FileResults);
        if (!File.Exists(currentFileResultsPath)) currentFileResultsPath = Path.Combine(artifactDirectory, FileNamesResolver.DefaultFileResults);
        
        var archiveFileResultsPath = Path.Combine(selection.ComparePath, fileNames.FileResults);
        if (!File.Exists(archiveFileResultsPath)) archiveFileResultsPath = Path.Combine(selection.ComparePath, FileNamesResolver.DefaultFileResults);

        var reportPath = Path.Combine(artifactDirectory, "benchmark-comparison.md");
        var reportBuilder = new StringBuilder();

        void Report(string? line = null)
        {
            var text = line ?? string.Empty;
            Console.WriteLine(text);
            reportBuilder.AppendLine(text);
        }

        async Task FlushReportAsync()
        {
            Directory.CreateDirectory(artifactDirectory);
            await File.WriteAllTextAsync(reportPath, reportBuilder.ToString(), ct);
        }

        if (!File.Exists(currentFileResultsPath))
        {
            Report($"No current file-level results found: {currentFileResultsPath}");
            await FlushReportAsync();
            return;
        }

        if (!File.Exists(archiveFileResultsPath))
        {
            Report($"No archive file-level results found: {archiveFileResultsPath}");
            await FlushReportAsync();
            return;
        }

        var currentRecords = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkFileCopyRecord>(currentFileResultsPath, ct);
        var archiveRecords = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkFileCopyRecord>(archiveFileResultsPath, ct);

        if (currentRecords.Count == 0 || archiveRecords.Count == 0)
        {
            Report("Current or archive records are empty.");
            await FlushReportAsync();
            return;
        }

        var scenarioOrder = BenchmarkHelpers.BuildScenarioOrder(config, config.Scenarios.Where(s => s.Enabled).Select(s => s.Name));
        var scenariosToAnalyze = !string.IsNullOrWhiteSpace(selection.ScenarioName)
            ? [selection.ScenarioName.Trim()]
            : scenarioOrder;

        if (scenariosToAnalyze.Count == 0)
        {
            scenariosToAnalyze = currentRecords.Select(r => r.ScenarioName)
                .Intersect(archiveRecords.Select(r => r.ScenarioName), StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var buckets = config.DatasetPreparation?.Buckets?.Select(b => new FileSizeBucket(b.MinimumFileSizeBytes, b.MaximumFileSizeBytes, b.Name)).ToList()
                      ?? FileSizeBuckets.All.ToList();

        Report("# Benchmark Comparison Report");
        Report($"- **Current:** `{artifactDirectory}`");
        Report($"- **Archive:** `{selection.ComparePath}`");
        Report();

        foreach (var scenarioName in scenariosToAnalyze)
        {
            Report($"## Scenario: `{scenarioName}`");

            var currentScenarioRecords = currentRecords.Where(r => string.Equals(r.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase)).ToList();
            var archiveScenarioRecords = archiveRecords.Where(r => string.Equals(r.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase)).ToList();

            var variants = currentScenarioRecords.Select(r => r.VariantName)
                .Intersect(archiveScenarioRecords.Select(r => r.VariantName), StringComparer.OrdinalIgnoreCase)
                .Where(v => string.IsNullOrWhiteSpace(selection.VariantName) || string.Equals(v, selection.VariantName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (variants.Count == 0)
            {
                Report("No common variants to compare.");
                Report();
                continue;
            }

            foreach (var variant in variants)
            {
                Report($"### Variant: `{variant}`");
                Report();

                // 1. Bucket Level Summary
                Report("#### Bucket Summary (Median MiB/s)");
                Report("| Bucket | Archive MiB/s | Current MiB/s | Delta % |");
                Report("|---|---:|---:|---:|");

                foreach (var bucket in buckets)
                {
                    var currentEvidence = BenchmarkStatistics.BuildBucketEvidence(currentScenarioRecords, bucket, variant);
                    var archiveEvidence = BenchmarkStatistics.BuildBucketEvidence(archiveScenarioRecords, bucket, variant);

                    if (archiveEvidence.RecordCount > 0 || currentEvidence.RecordCount > 0)
                    {
                        var delta = "";
                        if (archiveEvidence.AggregateThroughputMiBPerSecond > 0 && currentEvidence.AggregateThroughputMiBPerSecond > 0)
                        {
                            var diff = ((currentEvidence.AggregateThroughputMiBPerSecond - archiveEvidence.AggregateThroughputMiBPerSecond) / archiveEvidence.AggregateThroughputMiBPerSecond) * 100.0;
                            delta = $"{diff:+0.00;-0.00;0.00}%";
                        }
                        
                        Report($"| {bucket.Label} | {archiveEvidence.AggregateThroughputMiBPerSecond:0.00} | {currentEvidence.AggregateThroughputMiBPerSecond:0.00} | {delta} |");
                    }
                }
                Report();

                // 2. File Level Top Regressions
                Report("#### Top 50 File Regressions (by absolute median duration)");
                
                var currentByFile = currentScenarioRecords
                    .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(r => r.SourceRelativePath)
                    .Select(g => new { 
                        File = g.Key, 
                        Size = g.First().FileSizeBytes, 
                        MedianMs = BenchmarkHelpers.Percentile(g.Select(r => r.CopyDurationMilliseconds).OrderBy(v => v).ToList(), 0.50)
                    })
                    .ToDictionary(x => x.File, x => x);

                var archiveByFile = archiveScenarioRecords
                    .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
                    .GroupBy(r => r.SourceRelativePath)
                    .Select(g => new { 
                        File = g.Key, 
                        Size = g.First().FileSizeBytes, 
                        MedianMs = BenchmarkHelpers.Percentile(g.Select(r => r.CopyDurationMilliseconds).OrderBy(v => v).ToList(), 0.50)
                    })
                    .ToDictionary(x => x.File, x => x);

                var fileDeltas = new List<(string File, long Size, double ArchiveMs, double CurrentMs, double DeltaMs, double DeltaPercent)>();

                foreach (var curr in currentByFile.Values)
                {
                    if (archiveByFile.TryGetValue(curr.File, out var arch) && arch.MedianMs > 0)
                    {
                        var deltaMs = curr.MedianMs - arch.MedianMs;
                        var deltaPct = (deltaMs / arch.MedianMs) * 100.0;
                        fileDeltas.Add((curr.File, curr.Size, arch.MedianMs, curr.MedianMs, deltaMs, deltaPct));
                    }
                }

                var topRegressions = fileDeltas
                    .Where(d => d.DeltaMs > 0.5) // Only meaningful differences
                    .OrderByDescending(d => d.DeltaMs)
                    .Take(50)
                    .ToList();

                if (topRegressions.Count == 0)
                {
                    Report("No significant file regressions found (> 0.5ms difference).");
                }
                else
                {
                    Report("| File | Size | Archive (ms) | Current (ms) | Delta (ms) | Delta % |");
                    Report("|---|---:|---:|---:|---:|---:|");

                    foreach (var item in topRegressions)
                    {
                        Report($"| `{item.File}` | {BenchmarkHelpers.FormatBytesHuman(item.Size)} | {item.ArchiveMs:0.00} | {item.CurrentMs:0.00} | {item.DeltaMs:+0.00} | {item.DeltaPercent:+0.0}% |");
                    }
                }
                Report();
            }
        }

        await FlushReportAsync();
        Console.WriteLine($"Compare report written to: {reportPath}");
    }
}
