using System.Globalization;
using System.Text;

namespace SmartCopy.Benchmarks;

public sealed record SizeScalingInputRecord(
    string ScenarioName,
    string VariantName,
    string SourceRelativePath,
    long FileSizeBytes,
    double CopyDurationMilliseconds,
    double? ThroughputMiBPerSecond);

public sealed record SizeScalingAnalysisOptions(
    int MinimumSampleCount = 5,
    IReadOnlyList<long>? CoverageThresholdsBytes = null)
{
    public IReadOnlyList<long> ResolvedCoverageThresholdsBytes =>
        CoverageThresholdsBytes ?? [512L * 1024 * 1024, 1024L * 1024 * 1024, 2L * 1024 * 1024 * 1024];
}

public sealed record SizeScalingReport(
    IReadOnlyList<SizeScalingGroupReport> Groups,
    IReadOnlyList<SizeScalingCoverageRow> Coverage);

public sealed record SizeScalingGroupReport(
    string ScenarioName,
    string VariantName,
    IReadOnlyList<SizeScalingBucketRow> Buckets,
    IReadOnlyList<SizeScalingAdjacentComparison> AdjacentComparisons);

public sealed record SizeScalingBucketRow(
    string Label,
    long MinExclusiveBytes,
    long MaxInclusiveBytes,
    int RecordCount,
    int UniqueFileCount,
    long TotalBytes,
    double TotalDurationMilliseconds,
    double? AvgThroughputMiBPerSecond,
    double? P50ThroughputMiBPerSecond,
    double? P95ThroughputMiBPerSecond,
    double? P50DurationMilliseconds,
    double? P95DurationMilliseconds);

public sealed record SizeScalingAdjacentComparison(
    string FromBucket,
    string ToBucket,
    int FromRecordCount,
    int ToRecordCount,
    double? P50ThroughputRatio,
    double? P95ThroughputRatio,
    SizeScalingComparisonConfidence Confidence);

public sealed record SizeScalingCoverageRow(
    string Label,
    long ThresholdBytes,
    int RecordCount,
    int UniqueFileCount,
    long? MaxFileSizeBytes);

public enum SizeScalingComparisonConfidence
{
    Measured,
    InsufficientSamples,
}

public static class BenchmarkSizeScalingAnalysis
{
    public static SizeScalingReport Analyze(
        IEnumerable<SizeScalingInputRecord> records,
        SizeScalingAnalysisOptions? options = null)
    {
        var resolvedOptions = options ?? new SizeScalingAnalysisOptions();
        if (resolvedOptions.MinimumSampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Minimum sample count must be positive.");
        }

        var validRecords = records
            .Where(r => r.FileSizeBytes >= 0)
            .Where(r => r.CopyDurationMilliseconds > 0)
            .Where(r => r.ThroughputMiBPerSecond is not null)
            .ToList();

        var maxSize = validRecords.Count > 0 ? validRecords.Max(r => r.FileSizeBytes) : 0;
        var buckets = BuildPowerOfTwoBuckets(maxSize);

        var groupReports = validRecords
            .GroupBy(r => new { r.ScenarioName, r.VariantName })
            .OrderBy(g => g.Key.ScenarioName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.VariantName, StringComparer.OrdinalIgnoreCase)
            .Select(g => BuildGroupReport(g.Key.ScenarioName, g.Key.VariantName, g, buckets, resolvedOptions))
            .ToList();

        var coverage = resolvedOptions.ResolvedCoverageThresholdsBytes
            .OrderBy(t => t)
            .Select(t => BuildCoverageRow(validRecords, t))
            .ToList();

        return new SizeScalingReport(groupReports, coverage);
    }

    public static string ToMarkdown(SizeScalingReport report, string inputPath)
    {
        var markdown = new StringBuilder();
        markdown.AppendLine("## Size Scaling Analysis");
        markdown.AppendLine($"- **Input:** `{inputPath}`");
        markdown.AppendLine($"- **Groups:** `{report.Groups.Count}`");
        markdown.AppendLine();

        markdown.AppendLine("### Large-file Coverage");
        markdown.AppendLine("| Threshold | Records | Unique Files | Max Observed Size |");
        markdown.AppendLine("|---|---:|---:|---:|");
        foreach (var row in report.Coverage)
        {
            markdown.AppendLine(
                $"| {row.Label} | {row.RecordCount} | {row.UniqueFileCount} | {(row.MaxFileSizeBytes is long max ? FormatBytes(max) : "-")} |");
        }

        markdown.AppendLine();

        foreach (var group in report.Groups)
        {
            markdown.AppendLine($"## Scenario `{group.ScenarioName}`, Variant `{group.VariantName}`");
            markdown.AppendLine();
            markdown.AppendLine("### Bucket Stats");
            markdown.AppendLine("| Size Bucket | Records | Unique Files | Total Bytes | Total Duration | Avg MiB/s | P50 MiB/s | P95 MiB/s | P50 ms | P95 ms |");
            markdown.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var bucket in group.Buckets)
            {
                markdown.AppendLine(
                    $"| {bucket.Label} | {bucket.RecordCount} | {bucket.UniqueFileCount} | {FormatBytes(bucket.TotalBytes)} | {FormatMilliseconds(bucket.TotalDurationMilliseconds)} | {FormatDouble(bucket.AvgThroughputMiBPerSecond)} | {FormatDouble(bucket.P50ThroughputMiBPerSecond)} | {FormatDouble(bucket.P95ThroughputMiBPerSecond)} | {FormatDouble(bucket.P50DurationMilliseconds)} | {FormatDouble(bucket.P95DurationMilliseconds)} |");
            }

            markdown.AppendLine();
            markdown.AppendLine("### Adjacent Bucket Ratios");
            markdown.AppendLine("| From | To | Records | P50 MiB/s Ratio | P95 MiB/s Ratio | Confidence |");
            markdown.AppendLine("|---|---|---:|---:|---:|---|");
            foreach (var comparison in group.AdjacentComparisons)
            {
                markdown.AppendLine(
                    $"| {comparison.FromBucket} | {comparison.ToBucket} | {comparison.FromRecordCount}/{comparison.ToRecordCount} | {FormatDouble(comparison.P50ThroughputRatio)} | {FormatDouble(comparison.P95ThroughputRatio)} | {comparison.Confidence} |");
            }

            markdown.AppendLine();
        }

        return markdown.ToString();
    }

    private static SizeScalingGroupReport BuildGroupReport(
        string scenarioName,
        string variantName,
        IEnumerable<SizeScalingInputRecord> records,
        IReadOnlyList<SizeBucketDefinition> buckets,
        SizeScalingAnalysisOptions options)
    {
        var bucketRows = buckets
            .Select(bucket => BuildBucketRow(bucket, records.Where(r => bucket.Contains(r.FileSizeBytes)).ToList()))
            .Where(row => row.RecordCount > 0)
            .ToList();

        var comparisons = new List<SizeScalingAdjacentComparison>();
        for (var i = 1; i < bucketRows.Count; i++)
        {
            var previous = bucketRows[i - 1];
            var current = bucketRows[i];
            var confidence =
                previous.RecordCount >= options.MinimumSampleCount &&
                current.RecordCount >= options.MinimumSampleCount
                    ? SizeScalingComparisonConfidence.Measured
                    : SizeScalingComparisonConfidence.InsufficientSamples;

            comparisons.Add(new SizeScalingAdjacentComparison(
                previous.Label,
                current.Label,
                previous.RecordCount,
                current.RecordCount,
                Ratio(current.P50ThroughputMiBPerSecond, previous.P50ThroughputMiBPerSecond),
                Ratio(current.P95ThroughputMiBPerSecond, previous.P95ThroughputMiBPerSecond),
                confidence));
        }

        return new SizeScalingGroupReport(scenarioName, variantName, bucketRows, comparisons);
    }

    private static SizeScalingBucketRow BuildBucketRow(
        SizeBucketDefinition bucket,
        IReadOnlyList<SizeScalingInputRecord> records)
    {
        var throughputs = records
            .Select(r => r.ThroughputMiBPerSecond!.Value)
            .OrderBy(v => v)
            .ToList();
        var durations = records
            .Select(r => r.CopyDurationMilliseconds)
            .OrderBy(v => v)
            .ToList();

        return new SizeScalingBucketRow(
            bucket.Label,
            bucket.MinExclusiveBytes,
            bucket.MaxInclusiveBytes,
            records.Count,
            records.Select(r => r.SourceRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            records.Sum(r => r.FileSizeBytes),
            records.Sum(r => r.CopyDurationMilliseconds),
            throughputs.Count > 0 ? throughputs.Average() : null,
            PercentileOrNull(throughputs, 0.50),
            PercentileOrNull(throughputs, 0.95),
            PercentileOrNull(durations, 0.50),
            PercentileOrNull(durations, 0.95));
    }

    private static SizeScalingCoverageRow BuildCoverageRow(
        IReadOnlyList<SizeScalingInputRecord> records,
        long thresholdBytes)
    {
        var matching = records
            .Where(r => r.FileSizeBytes >= thresholdBytes)
            .ToList();

        return new SizeScalingCoverageRow(
            $">={FormatBytes(thresholdBytes)}",
            thresholdBytes,
            matching.Count,
            matching.Select(r => r.SourceRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            matching.Count > 0 ? matching.Max(r => r.FileSizeBytes) : null);
    }

    private static IReadOnlyList<SizeBucketDefinition> BuildPowerOfTwoBuckets(long maxSizeBytes)
    {
        var buckets = new List<SizeBucketDefinition>
        {
            new("0 B", -1, 0),
        };

        if (maxSizeBytes <= 0)
        {
            return buckets;
        }

        long lower = 0;
        long upper = 1024;
        while (true)
        {
            buckets.Add(new SizeBucketDefinition(BuildRangeLabel(lower, upper), lower, upper));
            if (upper >= maxSizeBytes || upper > long.MaxValue / 2)
            {
                break;
            }

            lower = upper;
            upper *= 2;
        }

        return buckets;
    }

    private static string BuildRangeLabel(long lowerExclusiveBytes, long upperInclusiveBytes)
    {
        if (lowerExclusiveBytes == 0)
        {
            return $"1 B-{FormatBytes(upperInclusiveBytes)}";
        }

        return $">{FormatBytes(lowerExclusiveBytes)}-{FormatBytes(upperInclusiveBytes)}";
    }

    private static double? PercentileOrNull(IReadOnlyList<double> sortedValuesAscending, double percentile)
    {
        if (sortedValuesAscending.Count == 0)
        {
            return null;
        }

        if (sortedValuesAscending.Count == 1)
        {
            return sortedValuesAscending[0];
        }

        var clamped = Math.Clamp(percentile, 0d, 1d);
        var index = (sortedValuesAscending.Count - 1) * clamped;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper)
        {
            return sortedValuesAscending[lower];
        }

        var weight = index - lower;
        return sortedValuesAscending[lower] + ((sortedValuesAscending[upper] - sortedValuesAscending[lower]) * weight);
    }

    private static double? Ratio(double? numerator, double? denominator)
    {
        if (numerator is null || denominator is null || denominator <= 0)
        {
            return null;
        }

        return numerator.Value / denominator.Value;
    }

    private static string FormatDouble(double? value) =>
        value is null ? "-" : value.Value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatMilliseconds(double value) =>
        value >= 1000
            ? TimeSpan.FromMilliseconds(value).ToString(@"m\:ss\.fff", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture) + " ms";

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        string[] units = ["KiB", "MiB", "GiB", "TiB"];
        var value = bytes / 1024d;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        return value.ToString(value >= 10 ? "0.#" : "0.##", CultureInfo.InvariantCulture) + " " + units[unitIndex];
    }

    private sealed record SizeBucketDefinition(string Label, long MinExclusiveBytes, long MaxInclusiveBytes)
    {
        public bool Contains(long sizeBytes) =>
            sizeBytes > MinExclusiveBytes && sizeBytes <= MaxInclusiveBytes;
    }
}
