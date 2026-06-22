namespace SmartCopy.Benchmarks;

internal static class BenchmarkStatistics
{
    /// <summary>
    /// Builds run-level evidence (median, mean, min, max, spread) for a specific variant
    /// from a pre-filtered list of converged runs for a single scenario.
    /// </summary>
    public static RunVariantEvidence BuildRunEvidence(
        IReadOnlyList<BenchmarkRunRecord> runs,
        string variant)
    {
        var variantRuns = runs
            .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var validDurations = variantRuns
            .Where(BenchmarkHelpers.IsSuccessfulRun)
            .Select(r => r.ExecuteDuration.TotalSeconds)
            .OrderBy(v => v)
            .ToList();

        if (validDurations.Count == 0)
        {
            return new RunVariantEvidence(
                variant,
                variantRuns.Count,
                0,
                variantRuns.Count,
                0, 0, 0, 0, 0);
        }

        var min = validDurations[0];
        var max = validDurations[^1];
        return new RunVariantEvidence(
            variant,
            variantRuns.Count,
            validDurations.Count,
            variantRuns.Count - validDurations.Count,
            BenchmarkHelpers.Percentile(validDurations, 0.50),
            validDurations.Average(),
            min,
            max,
            validDurations.Count > 1 ? validDurations[^1] - validDurations[0] : 0.0);
    }

    /// <summary>
    /// Builds file-level bucket evidence for a specific variant from a pre-filtered list
    /// of converged file records. Pass <paramref name="bucket"/> as <c>null</c> to
    /// aggregate across all file sizes.
    /// </summary>
    public static BucketVariantEvidence BuildBucketEvidence(
        IReadOnlyList<BenchmarkFileCopyRecord> records,
        FileSizeBucket? bucket,
        string variant)
    {
        var bucketRecords = records
            .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
            .Where(r => bucket is null || bucket.Contains(r.FileSizeBytes))
            .ToList();

        if (bucketRecords.Count == 0)
            return BucketVariantEvidence.Empty(bucket?.Label ?? "Scenario", variant);

        var durations = bucketRecords
            .Select(r => r.CopyDurationMilliseconds)
            .Where(v => v > 0)
            .OrderBy(v => v)
            .ToList();
        var runMedians = bucketRecords
            .GroupBy(r => (r.RunStartedUtc, r.RunIndex))
            .Select(g => g
                .Select(r => r.CopyDurationMilliseconds)
                .Where(v => v > 0)
                .OrderBy(v => v)
                .ToList())
            .Where(values => values.Count > 0)
            .Select(values => BenchmarkHelpers.Percentile(values, 0.50))
            .OrderBy(v => v)
            .ToList();

        // Aggregate throughput: median of per-run (totalBytes / totalSeconds)
        var runThroughputs = bucketRecords
            .GroupBy(r => r.RunIndex)
            .Select(g =>
            {
                var runBytes = g.Sum(r => r.FileSizeBytes);
                var runSeconds = g.Sum(r => r.CopyDurationMilliseconds) / 1000.0;
                return runBytes > 0 && runSeconds > 0 ? (runBytes / 1048576.0) / runSeconds : 0.0;
            })
            .OrderBy(t => t)
            .ToList();

        var aggregateThroughput = runThroughputs.Count > 0
            ? BenchmarkHelpers.Percentile(runThroughputs, 0.50)
            : 0.0;

        var totalBytes = bucketRecords.Sum(r => r.FileSizeBytes);

        return new BucketVariantEvidence(
            bucket?.Label ?? "Scenario",
            variant,
            bucketRecords.Count,
            totalBytes,
            durations.Sum(),
            durations.Count > 0 ? durations.Average() : 0.0,
            durations.Count > 0 ? BenchmarkHelpers.Percentile(durations, 0.50) : 0.0,
            durations.Count > 0 ? BenchmarkHelpers.Percentile(durations, 0.95) : 0.0,
            aggregateThroughput,
            runMedians.Count > 1 ? runMedians[^1] - runMedians[0] : 0.0,
            runThroughputs.Count > 1 ? runThroughputs[^1] - runThroughputs[0] : 0.0);
    }
}
