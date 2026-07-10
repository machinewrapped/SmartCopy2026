namespace SmartCopy.Benchmarks;

internal static class CrossScenarioAnalysisReporter
{
    public static void Write(
        Action<string?> report,
        BenchmarkConfig config,
        IReadOnlyList<BenchmarkRunRecord> runs,
        IReadOnlyList<BenchmarkFileCopyRecord> records,
        IReadOnlyList<FileSizeBucket> buckets,
        IReadOnlyList<string> variants,
        IReadOnlyList<string> scenarios)
    {
        report("## All Scenarios Summary");
        report(null);
        report("Cross-scenario summary matrix displaying median execute durations (for entire runs) and run-median aggregate throughput (MiB/s) per bucket.");
        report(null);

        var header = "| Variant | " + string.Join(" | ", scenarios.Select(BenchmarkHelpers.EscapeTable)) + " |";
        var divider = "|---|" + string.Join("|", scenarios.Select(_ => "---:")) + "|";
        WriteRunDurationMatrix(report, config, runs, variants, scenarios, header, divider);
        WriteThroughputMatrix(report, config, records, variants, scenarios, null, "Scenario-Level Median Throughput (MiB/s)", header, divider);

        foreach (var bucket in buckets.Where(bucket => records.Any(record => bucket.Contains(record.FileSizeBytes))))
            WriteThroughputMatrix(report, config, records, variants, scenarios, bucket, $"Bucket Throughput (MiB/s): {bucket.Label}", header, divider);
    }

    private static void WriteRunDurationMatrix(Action<string?> report, BenchmarkConfig config, IReadOnlyList<BenchmarkRunRecord> runs, IReadOnlyList<string> variants, IReadOnlyList<string> scenarios, string header, string divider)
    {
        report("### Run-Level Median Duration");
        report(null);
        report(header);
        report(divider);
        foreach (var variant in variants)
        {
            var control = GetMatchedControl(config, variant);
            var row = new List<string> { BenchmarkHelpers.EscapeTable(variant) };
            foreach (var scenario in scenarios)
            {
                var evidence = BenchmarkStatistics.BuildRunEvidence(Find(runs, scenario, variant), variant);
                row.Add(evidence.TotalRuns == 0 ? "-" : FormatDuration(evidence, string.IsNullOrEmpty(control) ? null : BenchmarkStatistics.BuildRunEvidence(Find(runs, scenario, control), control)));
            }
            WriteRow(report, row);
        }
        report(null);
    }

    private static void WriteThroughputMatrix(Action<string?> report, BenchmarkConfig config, IReadOnlyList<BenchmarkFileCopyRecord> records, IReadOnlyList<string> variants, IReadOnlyList<string> scenarios, FileSizeBucket? bucket, string title, string header, string divider)
    {
        report($"### {title}");
        report(null);
        report(header);
        report(divider);
        foreach (var variant in variants)
        {
            var control = GetMatchedControl(config, variant);
            var row = new List<string> { BenchmarkHelpers.EscapeTable(variant) };
            foreach (var scenario in scenarios)
            {
                var evidence = BenchmarkStatistics.BuildBucketEvidence(Find(records, scenario, variant), bucket, variant);
                row.Add(evidence.RecordCount == 0 ? "-" : FormatThroughput(evidence, string.IsNullOrEmpty(control) ? null : BenchmarkStatistics.BuildBucketEvidence(Find(records, scenario, control), bucket, control)));
            }
            WriteRow(report, row);
        }
        report(null);
    }

    private static string? GetMatchedControl(BenchmarkConfig config, string variant) =>
        config.Variants.FirstOrDefault(value => string.Equals(value.Name, variant, StringComparison.OrdinalIgnoreCase))?.MatchedControl;

    private static IReadOnlyList<BenchmarkRunRecord> Find(
        IReadOnlyList<BenchmarkRunRecord> values,
        string scenario,
        string variant) =>
        values.Where(value =>
            string.Equals(value.ScenarioName, scenario, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(value.VariantName, variant, StringComparison.OrdinalIgnoreCase)).ToList();

    private static IReadOnlyList<BenchmarkFileCopyRecord> Find(
        IReadOnlyList<BenchmarkFileCopyRecord> values,
        string scenario,
        string variant) =>
        values.Where(value =>
            string.Equals(value.ScenarioName, scenario, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(value.VariantName, variant, StringComparison.OrdinalIgnoreCase)).ToList();

    private static void WriteRow(Action<string?> report, List<string> row)
    {
        if (row.Skip(1).Any(value => value != "-"))
            report("| " + string.Join(" | ", row) + " |");
    }

    private static string FormatDuration(RunVariantEvidence evidence, RunVariantEvidence? control)
    {
        var value = BenchmarkHelpers.FormatDurationHuman(evidence.MedianSeconds);
        if (control is null || control.TotalRuns == 0 || control.MedianSeconds <= 0)
            return value;
        var delta = (evidence.MedianSeconds - control.MedianSeconds) / control.MedianSeconds * 100.0;
        return $"{value} ({(delta > 0 ? "+" : "")}{delta:0.0}%)";
    }

    private static string FormatThroughput(BucketVariantEvidence evidence, BucketVariantEvidence? control)
    {
        var value = $"{evidence.AggregateThroughputMiBPerSecond:0.00}";
        if (control is null || control.RecordCount == 0 || control.AggregateThroughputMiBPerSecond <= 0)
            return value;
        var delta = (evidence.AggregateThroughputMiBPerSecond - control.AggregateThroughputMiBPerSecond) / control.AggregateThroughputMiBPerSecond * 100.0;
        return $"{value} ({(delta > 0 ? "+" : "")}{delta:0.0}%)";
    }
}
