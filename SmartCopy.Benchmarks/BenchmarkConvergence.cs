namespace SmartCopy.Benchmarks;

internal static class BenchmarkConvergence
{
    internal enum Status
    {
        NotConverged,
        Converged,
        GaveUp,
    }

    /// <summary>
    /// A detected split of a variant's run durations into a low cluster and a high cluster
    /// separated by a dominant gap (seconds). Signals that the distribution is multimodal
    /// (e.g. cache-warm runs vs cold runs), so a converged window may represent only one mode.
    /// </summary>
    internal sealed record DistributionPartition(
        int LowCount, double LowMinSeconds, double LowMaxSeconds,
        int HighCount, double HighMinSeconds, double HighMaxSeconds,
        double GapSeconds);

    /// <summary>
    /// Heuristic multimodality test. Splits the duration-sorted runs at their largest interior
    /// gap (keeping at least <paramref name="minClusterSize"/> runs on each side) and reports a
    /// partition only when that gap both dominates every other gap (by
    /// <paramref name="dominanceFactor"/>) and is large relative to the median
    /// (<paramref name="minRelativeGap"/>). Returns <c>null</c> for unimodal or merely noisy
    /// distributions (a lone outlier is not a partition — convergence already handles those).
    /// </summary>
    public static DistributionPartition? DetectMultimodal(
        IReadOnlyList<double> durationsSeconds,
        double dominanceFactor = 2.0,
        double minRelativeGap = 0.15,
        int minClusterSize = 2)
    {
        var d = durationsSeconds.OrderBy(x => x).ToList();
        var n = d.Count;
        if (n < 2 * minClusterSize)
            return null;

        var lo = minClusterSize - 1;
        var hi = n - 1 - minClusterSize;

        var splitIdx = -1;
        var bestGap = -1.0;
        for (var i = lo; i <= hi; i++)
        {
            var gap = d[i + 1] - d[i];
            if (gap > bestGap)
            {
                bestGap = gap;
                splitIdx = i;
            }
        }

        if (splitIdx < 0)
            return null;

        var refGap = 0.0;
        for (var i = 0; i < n - 1; i++)
            if (i != splitIdx)
                refGap = Math.Max(refGap, d[i + 1] - d[i]);

        var median = BenchmarkHelpers.Percentile(d, 0.5);
        if (bestGap < dominanceFactor * refGap)
            return null;
        if (median > 0 && bestGap < minRelativeGap * median)
            return null;

        return new DistributionPartition(
            splitIdx + 1, d[0], d[splitIdx],
            n - 1 - splitIdx, d[splitIdx + 1], d[^1],
            bestGap);
    }

    /// <summary>
    /// Determines convergence status for a specific scenario/variant pair.
    /// Used by the run selector to decide whether to schedule more runs.
    /// </summary>
    public static Status Check(
        IReadOnlyList<BenchmarkRunRecord> allRuns,
        string scenarioName,
        BenchmarkVariant variant,
        BenchmarkConfig config)
    {
        var successfulRuns = allRuns
            .Where(r => BenchmarkHelpers.IsSuccessfulRunForScenarioVariant(r, scenarioName, variant.Name))
            .ToList();

        if (successfulRuns.Count < variant.DesiredRunCount)
            return Status.NotConverged;

        if (!config.Converge)
            return Status.Converged;

        var sorted = successfulRuns
            .OrderBy(r => r.ExecuteDuration.TotalSeconds)
            .ToList();

        var (_, _, metThreshold) = FindTightestWindow(sorted, variant.DesiredRunCount, config.ConvergenceSpreadPercent);

        if (metThreshold)
            return Status.Converged;

        return successfulRuns.Count >= variant.DesiredRunCount + config.MaxConvergenceRuns
            ? Status.GaveUp
            : Status.NotConverged;
    }

    /// <summary>
    /// Returns the RunIndex values forming the tightest converged window of
    /// <paramref name="desiredRunCount"/> runs. When fewer successful runs exist than
    /// desired, all are returned. When no window meets the spread threshold (GaveUp),
    /// the tightest available window is returned so analysis always uses the best data.
    /// </summary>
    public static HashSet<int> GetConvergedRunIndexes(
        IReadOnlyList<BenchmarkRunRecord> successfulVariantRuns,
        int desiredRunCount,
        double convergenceSpreadPercent)
    {
        if (successfulVariantRuns.Count == 0)
            return [];

        if (successfulVariantRuns.Count <= desiredRunCount)
            return successfulVariantRuns.Select(r => r.RunIndex).ToHashSet();

        var sorted = successfulVariantRuns
            .OrderBy(r => r.ExecuteDuration.TotalSeconds)
            .ToList();

        var (bestWindow, _, _) = FindTightestWindow(sorted, desiredRunCount, convergenceSpreadPercent);

        return (bestWindow ?? sorted.Take(desiredRunCount).ToList())
            .Select(r => r.RunIndex)
            .ToHashSet();
    }

    /// <summary>
    /// Returns the minimum spread % achieved across all sliding windows for the given
    /// scenario/variant, for use in queue display. Returns <see cref="double.NaN"/> when
    /// fewer than 2 successful runs exist.
    /// </summary>
    public static double GetCurrentSpreadPercent(
        IReadOnlyList<BenchmarkRunRecord> allRuns,
        string scenarioName,
        BenchmarkVariant variant,
        BenchmarkConfig config)
    {
        var successfulRuns = allRuns
            .Where(r => BenchmarkHelpers.IsSuccessfulRunForScenarioVariant(r, scenarioName, variant.Name))
            .ToList();

        if (successfulRuns.Count < 2)
            return double.NaN;

        var sorted = successfulRuns
            .OrderBy(r => r.ExecuteDuration.TotalSeconds)
            .ToList();

        var W = Math.Min(variant.DesiredRunCount, sorted.Count);
        if (W < 2)
            return double.NaN;

        var (_, bestSpread, _) = FindTightestWindow(sorted, W, config.ConvergenceSpreadPercent);
        return bestSpread;
    }

    /// <summary>
    /// Per-variant converged-run-index sets for one scenario's terminal runs (the tightest
    /// window, or the best available when a variant gave up). Shared by the analysis report,
    /// the validation gate, and the validation conclusion so they select identical windows
    /// and cannot disagree on a verdict.
    /// </summary>
    public static Dictionary<string, HashSet<int>> GetConvergedIndexesForVariants(
        BenchmarkConfig config,
        IReadOnlyList<BenchmarkRunRecord> scenarioRuns,
        IReadOnlyList<string> variants)
    {
        return variants.ToDictionary(
            v => v,
            v =>
            {
                var successful = scenarioRuns
                    .Where(r => string.Equals(r.VariantName, v, StringComparison.OrdinalIgnoreCase))
                    .Where(BenchmarkHelpers.IsSuccessfulRun)
                    .ToList();
                return GetConvergedRunIndexes(
                    successful,
                    GetDesiredRunCount(config, v),
                    config.ConvergenceSpreadPercent);
            },
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns the configured <c>DesiredRunCount</c> for a variant name, defaulting to 3.
    /// </summary>
    public static int GetDesiredRunCount(BenchmarkConfig config, string variantName)
    {
        var variant = config.Variants.FirstOrDefault(v =>
            string.Equals(v.Name, variantName, StringComparison.OrdinalIgnoreCase));
        return variant?.DesiredRunCount ?? 3;
    }

    /// <summary>
    /// Slides a window of size <paramref name="W"/> over the duration-sorted run list,
    /// returning the tightest window found and whether it met the spread threshold.
    /// </summary>
    private static (List<BenchmarkRunRecord>? BestWindow, double BestSpread, bool MetThreshold)
        FindTightestWindow(List<BenchmarkRunRecord> sorted, int W, double threshold)
    {
        List<BenchmarkRunRecord>? bestWindow = null;
        double bestSpread = double.MaxValue;

        for (var i = 0; i <= sorted.Count - W; i++)
        {
            var window = sorted.Skip(i).Take(W).ToList();
            var windowMin = window[0].ExecuteDuration.TotalSeconds;
            var windowMax = window[W - 1].ExecuteDuration.TotalSeconds;
            var windowMedian = W % 2 == 1
                ? window[W / 2].ExecuteDuration.TotalSeconds
                : (window[W / 2 - 1].ExecuteDuration.TotalSeconds + window[W / 2].ExecuteDuration.TotalSeconds) / 2.0;

            var spreadPercent = windowMedian > 0
                ? ((windowMax - windowMin) / windowMedian) * 100.0
                : 0.0;

            if (spreadPercent < bestSpread)
            {
                bestSpread = spreadPercent;
                bestWindow = window;
            }

            if (spreadPercent <= threshold)
                return (bestWindow, bestSpread, true);
        }

        return (bestWindow, bestSpread == double.MaxValue ? double.NaN : bestSpread, false);
    }
}
