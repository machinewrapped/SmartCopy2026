using System.Text;
using SmartCopy.Core.Pipeline;

namespace SmartCopy.Benchmarks;

internal static class AnalysisRunner
{
    public static async Task RunAsync(
        string workingDirectory,
        BenchmarkConfig config,
        BenchmarkCliOptions selection,
        CancellationToken ct)
    {
        var fileNames = FileNamesResolver.GetFileNames(selection.ConfigPath);
        var artifactDirectory = BenchmarkHelpers.ResolveArtifactDirectory(workingDirectory, config.SourcePath, config.ArtifactPath);
        var resultsPath = Path.Combine(artifactDirectory, fileNames.Results);
        var fileResultsPath = Path.Combine(artifactDirectory, fileNames.FileResults);
        var analysisPath = Path.Combine(artifactDirectory, fileNames.Analysis);
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
            await File.WriteAllTextAsync(analysisPath, reportBuilder.ToString(), ct);
        }

        if (!File.Exists(fileResultsPath) && !File.Exists(resultsPath))
        {
            Report($"No benchmark results found: {resultsPath}");
            Report($"No file-level results found: {fileResultsPath}");
            Report("Run benchmark mode first to produce benchmark-results.ndjson and benchmark-file-results.ndjson.");
            await FlushReportAsync();
            return;
        }

        var allRuns = File.Exists(resultsPath)
            ? await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkRunRecord>(resultsPath, ct)
            : [];
        var allRecords = File.Exists(fileResultsPath)
            ? await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkFileCopyRecord>(fileResultsPath, ct)
            : [];

        if (allRuns.Count == 0 && allRecords.Count == 0)
        {
            Report("No benchmark records available.");
            await FlushReportAsync();
            return;
        }

        var scenarioOrder = BenchmarkHelpers.BuildScenarioOrder(config, config.Scenarios.Where(s => s.Enabled).Select(s => s.Name));
        var scenariosToAnalyze = !string.IsNullOrWhiteSpace(selection.ScenarioName)
            ? [selection.ScenarioName.Trim()]
            : scenarioOrder;

        if (scenariosToAnalyze.Count == 0)
        {
            scenariosToAnalyze = allRuns
                .Select(r => r.ScenarioName)
                .Concat(allRecords.Select(r => r.ScenarioName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var scenarioSet = new HashSet<string>(scenariosToAnalyze, StringComparer.OrdinalIgnoreCase);
        var filteredRecords = allRecords
            .Where(r => scenarioSet.Contains(r.ScenarioName))
            .Where(r => string.IsNullOrWhiteSpace(selection.VariantName) ||
                        string.Equals(r.VariantName, selection.VariantName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var filteredRuns = allRuns
            .Where(r => scenarioSet.Contains(r.ScenarioName))
            .Where(r => string.IsNullOrWhiteSpace(selection.VariantName) ||
                        string.Equals(r.VariantName, selection.VariantName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        scenariosToAnalyze = scenariosToAnalyze
            .Where(s => filteredRecords.Any(r => string.Equals(r.ScenarioName, s, StringComparison.OrdinalIgnoreCase)) ||
                        filteredRuns.Any(r => string.Equals(r.ScenarioName, s, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (filteredRecords.Count == 0 && filteredRuns.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(selection.ScenarioName))
                Report($"No records found for scenario '{selection.ScenarioName.Trim()}'.");
            else
                Report("No records found for the selected scenarios.");

            if (!string.IsNullOrWhiteSpace(selection.VariantName))
                Report($"Variant filter: '{selection.VariantName}'.");

            await FlushReportAsync();
            return;
        }

        var distinctVariants = filteredRecords
            .Select(r => r.VariantName)
            .Concat(filteredRuns.Select(r => r.VariantName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Order variants by config definition order; unknowns go last alphabetically.
        var variantIndexMap = config.Variants
            .Select((v, i) => (v.Name, i))
            .ToDictionary(t => t.Name, t => t.i, StringComparer.OrdinalIgnoreCase);

        var allVariants = distinctVariants
            .OrderBy(v => variantIndexMap.GetValueOrDefault(v, int.MaxValue))
            .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Report("## Analysis Summary");
        Report($"- **Mode:** `analysis`");
        Report($"- **Source:** `{Path.GetFullPath(config.SourcePath)}`");
        Report($"- **Scenario filter:** `{(string.IsNullOrWhiteSpace(selection.ScenarioName) ? "all (configured order)" : selection.ScenarioName.Trim())}`");
        Report($"- **Scenario count:** `{scenariosToAnalyze.Count}`");
        Report($"- **Run records:** `{filteredRuns.Count}`");
        Report($"- **File records:** `{filteredRecords.Count}`");
        Report($"- **Variants:** {string.Join(", ", allVariants.Select(v => $"`{v}`"))}");
        Report($"- **Run input:** `{resultsPath}`");
        Report($"- **File input:** `{fileResultsPath}`");
        Report();

        var buckets = config.DatasetPreparation?.Buckets?.Select(b => new FileSizeBucket(b.MinimumFileSizeBytes, b.MaximumFileSizeBytes, b.Name)).ToList()
                      ?? FileSizeBuckets.All.ToList();

        // Keyed by variant name → its explicit matched control name (only non-control variants present).
        var matchedControlLookup = config.Variants
            .Where(v => !string.IsNullOrWhiteSpace(v.MatchedControl))
            .ToDictionary(v => v.Name, v => v.MatchedControl!, StringComparer.OrdinalIgnoreCase);

        var allConvergedRuns = new List<BenchmarkRunRecord>();
        var allConvergedRecords = new List<BenchmarkFileCopyRecord>();

        foreach (var scenarioName in scenariosToAnalyze)
        {
            var records = filteredRecords
                .Where(r => string.Equals(r.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var runs = filteredRuns
                .Where(r => string.Equals(r.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase))
                .Where(BenchmarkHelpers.IsTerminalRun)
                .ToList();

            Report($"## Scenario: `{scenarioName}`");

            if (records.Count == 0 && runs.Count == 0)
            {
                Report("No records for this scenario.");
                Report();
                continue;
            }

            var variants = records
                .Select(r => r.VariantName)
                .Concat(runs.Select(r => r.VariantName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => variantIndexMap.GetValueOrDefault(v, int.MaxValue))
                .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Report($"- **Run records:** `{runs.Count}`");
            Report($"- **File records:** `{records.Count}`");
            Report($"- **Variants:** {string.Join(", ", variants.Select(v => $"`{v}`"))}");
            Report();

            var warnings = BuildMissingControlWarnings(variants, matchedControlLookup);

            var convergedIndexesByVariant = BenchmarkConvergence.GetConvergedIndexesForVariants(config, runs, variants);

            var convergedRuns = runs
                .Where(r => convergedIndexesByVariant.TryGetValue(r.VariantName, out var idx) && idx.Contains(r.RunIndex))
                .ToList();
            var convergedRecords = records
                .Where(r => convergedIndexesByVariant.TryGetValue(r.VariantName, out var idx) && idx.Contains(r.RunIndex))
                .ToList();

            ReportRunEvidence(Report, config, scenarioName, runs, convergedRuns, variants, config.GatePercent, matchedControlLookup);
            ReportBucketRecommendations(Report, convergedRecords, buckets, variants, warnings, matchedControlLookup, config.GatePercent);
            ReportBucketMetrics(Report, convergedRecords, buckets, variants);
            ReportBatchingIsolationEvidence(Report, convergedRecords, buckets, variants, config.GatePercent);

            allConvergedRuns.AddRange(convergedRuns);
            allConvergedRecords.AddRange(convergedRecords);

            if (warnings.Count > 0)
            {
                Report("### Missing Matched Controls");
                foreach (var warning in warnings)
                    Report($"- {warning}");
                Report();
            }

            Report();

            var htmlPath = Path.ChangeExtension(analysisPath, ".html");
            if (scenariosToAnalyze.Count > 1)
                htmlPath = Path.Combine(Path.GetDirectoryName(htmlPath) ?? "", $"{Path.GetFileNameWithoutExtension(htmlPath)}-{scenarioName}.html");

            await BenchmarkHtmlReportGenerator.GenerateAsync(htmlPath, scenarioName, buckets, variants, convergedRecords, convergedRuns, runs, matchedControlLookup);
        }

        if (scenariosToAnalyze.Count > 1)
        {
            ReportCrossScenarioSummary(Report, config, allConvergedRuns, allConvergedRecords, buckets, allVariants, scenariosToAnalyze);

            var summaryHtmlPath = Path.Combine(Path.GetDirectoryName(analysisPath) ?? "", $"{Path.GetFileNameWithoutExtension(analysisPath)}-summary.html");
            await BenchmarkHtmlReportGenerator.GenerateSummaryAsync(summaryHtmlPath, buckets, allVariants, allConvergedRecords, allConvergedRuns, scenariosToAnalyze, matchedControlLookup);
        }

        await FlushReportAsync();
        Console.WriteLine($"Analysis: {analysisPath}");
    }

    private static void ReportRunEvidence(
        Action<string?> report,
        BenchmarkConfig config,
        string scenarioName,
        IReadOnlyList<BenchmarkRunRecord> terminalRuns,
        IReadOnlyList<BenchmarkRunRecord> convergedRuns,
        IReadOnlyList<string> variants,
        double gatePercent,
        IReadOnlyDictionary<string, string> matchedControls)
    {
        report("### Run-Level Evidence");
        report(null);

        if (terminalRuns.Count == 0)
        {
            report("No run-level records available. Whole-policy wall-clock verdicts cannot be produced.");
            report(null);
            return;
        }

        report("_Median/Mean/Min/Max/Window Spread describe the **converged window only** — the tightest `used` of `ran` runs the selector kept; runs it discarded are not reflected in those columns. **Convergence** reports whether that window actually met the spread gate (`converged`) or the selector exhausted its attempts (`gave up`) — a `*` on a verdict means it rests on a window that never converged._");
        report(null);
        report("| Variant | Runs (used/ran) | Convergence | Selected Band | Median Execute | Mean Execute | Min | Max | Window Spread | Delta vs Control | Noise Floor | Verdict |");
        report("|---|---:|---|---|---:|---:|---:|---:|---:|---:|---:|---|");

        var anyUnconverged = false;

        foreach (var variant in variants)
        {
            var variantTerminal = terminalRuns
                .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (variantTerminal.Count == 0)
                continue;

            var evidence = BenchmarkStatistics.BuildRunEvidence(convergedRuns, variant);

            var isControl = !matchedControls.ContainsKey(variant);
            RunVariantEvidence? controlEvidence = null;
            if (!isControl && matchedControls.TryGetValue(variant, out var controlName))
            {
                var ce = BenchmarkStatistics.BuildRunEvidence(convergedRuns, controlName);
                if (ce.TotalRuns > 0) controlEvidence = ce;
            }

            var comparison = BenchmarkComparison.CompareRunEvidence(evidence, controlEvidence, gatePercent, isControl);

            var ran = variantTerminal.Count;
            var failed = variantTerminal.Count(r => !BenchmarkHelpers.IsSuccessfulRun(r));
            var successful = ran - failed;
            var used = evidence.ValidRuns;

            var variantConfig = config.Variants.FirstOrDefault(v =>
                string.Equals(v.Name, variant, StringComparison.OrdinalIgnoreCase));
            var desired = BenchmarkConvergence.GetDesiredRunCount(config, variant);
            var status = variantConfig is not null
                ? BenchmarkConvergence.Check(terminalRuns, scenarioName, variantConfig, config)
                : BenchmarkConvergence.Status.NotConverged;
            var spreadPercent = variantConfig is not null
                ? BenchmarkConvergence.GetCurrentSpreadPercent(terminalRuns, scenarioName, variantConfig, config)
                : double.NaN;

            var converged = status == BenchmarkConvergence.Status.Converged;
            if (!converged) anyUnconverged = true;

            var runsCell = failed > 0 ? $"{used}/{ran} ({failed} failed)" : $"{used}/{ran}";
            var convergenceCell = FormatConvergence(status, spreadPercent, successful, desired);
            var verdictCell = converged ? comparison.Verdict : $"{comparison.Verdict} \\*";
            var distribution = BenchmarkConvergence.BuildDistributionSummary(
                variantTerminal
                    .Where(BenchmarkHelpers.IsSuccessfulRun)
                    .Select(r => r.ExecuteDuration.TotalSeconds)
                    .ToList());
            var selectedBand = FormatSelectedBand(evidence, distribution);

            string Dur(double seconds) => used == 0 ? "-" : BenchmarkHelpers.FormatDurationHuman(seconds);

            report(
                $"| {BenchmarkHelpers.EscapeTable(variant)} | {runsCell} | {convergenceCell} | {selectedBand} | " +
                $"{Dur(evidence.MedianSeconds)} | {Dur(evidence.MeanSeconds)} | " +
                $"{Dur(evidence.MinSeconds)} | {Dur(evidence.MaxSeconds)} | " +
                $"{Dur(evidence.SpreadSeconds)} | {comparison.DeltaText} | {comparison.NoiseText} | {verdictCell} |");
        }

        report(null);
        if (anyUnconverged)
        {
            report("> ⚠ One or more variants did not converge (`gave up` / `not converged`). Their window statistics are the *tightest available* slice, not a stable measurement; treat `*`-marked verdicts as indicative only, not gate-quality evidence.");
            report(null);
        }

        report("### Run Distribution (All Successful Runs)");
        report(null);
        report("_This table uses every successful terminal run for the variant, including runs discarded by the converged-window selector above._");
        report(null);
        report("| Variant | Successful/Ran | All-Run Median | All-Run Mean | Global Min | Global Max | Global Spread | Cluster Count | Clusters |");
        report("|---|---:|---:|---:|---:|---:|---:|---:|---|");

        var clustered = new List<(string Variant, BenchmarkConvergence.DistributionSummary Summary)>();
        var distributionSummaries = new Dictionary<string, BenchmarkConvergence.DistributionSummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var variant in variants)
        {
            var variantTerminal = terminalRuns
                .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (variantTerminal.Count == 0)
                continue;

            var durations = variantTerminal
                .Where(BenchmarkHelpers.IsSuccessfulRun)
                .Select(r => r.ExecuteDuration.TotalSeconds)
                .ToList();

            var summary = BenchmarkConvergence.BuildDistributionSummary(durations);
            distributionSummaries[variant] = summary;
            if (summary.HasSeparatedClusters)
                clustered.Add((variant, summary));

            var successful = durations.Count;
            var runCount = variantTerminal.Count;
            var clustersText = summary.Clusters.Count == 0
                ? "-"
                : string.Join("; ", summary.Clusters.Select(FormatCluster));

            report(
                $"| {BenchmarkHelpers.EscapeTable(variant)} | {successful}/{runCount} | " +
                $"{FormatDistributionDuration(summary.MedianSeconds)} | " +
                $"{FormatDistributionDuration(summary.MeanSeconds)} | " +
                $"{FormatDistributionDuration(summary.MinSeconds)} | " +
                $"{FormatDistributionDuration(summary.MaxSeconds)} | " +
                $"{FormatDistributionDuration(summary.SpreadSeconds)} | " +
                $"{summary.Clusters.Count} | {BenchmarkHelpers.EscapeTable(clustersText)} |");
        }

        report(null);
        if (clustered.Count > 0)
        {
            report("### Run Mode Summary (All Successful Runs)");
            report(null);
            report("_Only variants with separated duration clusters are listed here. Fast and slow bands are the first and last clusters for each variant. Delta compares the same band against the variant's configured matched control when both variants have separated clusters._");
            report(null);
            report("| Variant | Fast Runs | Fast Median | Fast Range | Fast Delta vs Control | Slow Runs | Slow Median | Slow Range | Slow Delta vs Control |");
            report("|---|---:|---:|---:|---:|---:|---:|---:|---:|");

            foreach (var variant in variants)
            {
                if (!distributionSummaries.TryGetValue(variant, out var summary) || !summary.HasSeparatedClusters)
                    continue;

                var fast = summary.Clusters[0];
                var slow = summary.Clusters[^1];
                var fastDelta = FormatBandDelta(variant, BandSelector.Fast, distributionSummaries, matchedControls);
                var slowDelta = FormatBandDelta(variant, BandSelector.Slow, distributionSummaries, matchedControls);

                report(
                    $"| {BenchmarkHelpers.EscapeTable(variant)} | " +
                    $"{fast.Count} | {FormatDistributionDuration(fast.MedianSeconds)} | {BenchmarkHelpers.EscapeTable(FormatClusterRange(fast))} | {fastDelta} | " +
                    $"{slow.Count} | {FormatDistributionDuration(slow.MedianSeconds)} | {BenchmarkHelpers.EscapeTable(FormatClusterRange(slow))} | {slowDelta} |");
            }

            report(null);
            report("> ⚠ **Clustered run distribution** — these variants' successful runs split into separated clusters, so convergence may be selecting one mode (e.g. cache-warm vs cold). A verdict drawn from one cluster is not trustworthy until the split is explained:");
            foreach (var (variant, summary) in clustered)
            {
                report(
                    $">   - `{BenchmarkHelpers.EscapeTable(variant)}`: " +
                    $"{BenchmarkHelpers.EscapeTable(string.Join("; ", summary.Clusters.Select(FormatCluster)))}");
            }
            report(null);
        }

        ReportGcEvidence(report, terminalRuns, variants);

        static string FormatBandDelta(
            string variant,
            BandSelector selector,
            IReadOnlyDictionary<string, BenchmarkConvergence.DistributionSummary> summaries,
            IReadOnlyDictionary<string, string> controls)
        {
            if (!controls.TryGetValue(variant, out var controlVariant))
                return "-";
            if (!summaries.TryGetValue(variant, out var candidateSummary) ||
                !summaries.TryGetValue(controlVariant, out var controlSummary))
                return "-";
            if (candidateSummary.Clusters.Count < 2 || controlSummary.Clusters.Count < 2)
                return "-";

            var candidate = selector == BandSelector.Fast
                ? candidateSummary.Clusters[0]
                : candidateSummary.Clusters[^1];
            var control = selector == BandSelector.Fast
                ? controlSummary.Clusters[0]
                : controlSummary.Clusters[^1];

            if (control.MedianSeconds <= 0)
                return "-";

            var deltaSeconds = control.MedianSeconds - candidate.MedianSeconds;
            var percent = (deltaSeconds / control.MedianSeconds) * 100.0;
            return $"{BenchmarkComparison.FormatSignedPercent(percent)} ({BenchmarkComparison.FormatSignedDurationSeconds(deltaSeconds)})";
        }

        static string FormatSelectedBand(
            RunVariantEvidence evidence,
            BenchmarkConvergence.DistributionSummary distribution)
        {
            if (evidence.ValidRuns == 0 || distribution.Clusters.Count == 0)
                return "-";

            const double epsilon = 0.0005;
            var matches = distribution.Clusters
                .Select((cluster, index) => (cluster, index))
                .Where(item =>
                    evidence.MaxSeconds + epsilon >= item.cluster.MinSeconds &&
                    evidence.MinSeconds - epsilon <= item.cluster.MaxSeconds)
                .Select(item => item.index)
                .ToList();

            return matches.Count == 1
                ? FormatBandName(matches[0], distribution.Clusters.Count)
                : "Mixed";
        }

        static string FormatBandName(int index, int count)
        {
            if (count <= 1)
                return "Single";
            if (index == 0)
                return "Fast";
            if (index == count - 1)
                return "Slow";
            return "Bridge";
        }

        static string FormatDistributionDuration(double seconds) =>
            double.IsNaN(seconds) ? "-" : BenchmarkHelpers.FormatDurationHuman(seconds);

        static string FormatCluster(BenchmarkConvergence.DistributionCluster cluster)
        {
            return $"{cluster.Count} @ {FormatClusterRange(cluster)}";
        }

        static string FormatClusterRange(BenchmarkConvergence.DistributionCluster cluster)
        {
            var min = BenchmarkHelpers.FormatDurationHuman(cluster.MinSeconds);
            var max = BenchmarkHelpers.FormatDurationHuman(cluster.MaxSeconds);
            var range = Math.Abs(cluster.MaxSeconds - cluster.MinSeconds) < 0.0005
                ? min
                : $"{min}-{max}";
            return range;
        }
    }

    private static void ReportGcEvidence(
        Action<string?> report,
        IReadOnlyList<BenchmarkRunRecord> terminalRuns,
        IReadOnlyList<string> variants)
    {
        if (!terminalRuns.Any(r => BenchmarkHelpers.IsSuccessfulRun(r) && r.ExecuteAllocatedBytes.HasValue))
            return;

        report("### Execute GC Evidence (All Successful Runs)");
        report(null);
        report("_Captured around the execute window only. Allocation bytes are process counters, so they include work on benchmark/reporting threads during that window. Collection counts are reported as per-run medians/means so variants with different completed-run counts remain comparable._");
        report(null);
        report("| Variant | Runs with GC | Median Allocated | Mean Allocated | Max Allocated | Median Gen0 | Mean Gen0 | Median Gen1 | Mean Gen1 | Median Gen2 | Mean Gen2 | Median Heap Delta | Median Fragmentation Delta |");
        report("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var variant in variants)
        {
            var runs = terminalRuns
                .Where(BenchmarkHelpers.IsSuccessfulRun)
                .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
                .Where(r => r.ExecuteAllocatedBytes.HasValue)
                .ToList();
            if (runs.Count == 0)
                continue;

            var allocated = runs.Select(r => (double)r.ExecuteAllocatedBytes!.Value).OrderBy(v => v).ToList();
            var gen0 = runs.Select(r => (double)(r.ExecuteGen0Collections ?? 0)).OrderBy(v => v).ToList();
            var gen1 = runs.Select(r => (double)(r.ExecuteGen1Collections ?? 0)).OrderBy(v => v).ToList();
            var gen2 = runs.Select(r => (double)(r.ExecuteGen2Collections ?? 0)).OrderBy(v => v).ToList();
            var heapDeltas = runs
                .Where(r => r.ExecuteHeapSizeDeltaBytes.HasValue)
                .Select(r => (double)r.ExecuteHeapSizeDeltaBytes!.Value)
                .OrderBy(v => v)
                .ToList();
            var fragmentedDeltas = runs
                .Where(r => r.ExecuteFragmentedDeltaBytes.HasValue)
                .Select(r => (double)r.ExecuteFragmentedDeltaBytes!.Value)
                .OrderBy(v => v)
                .ToList();

            report(
                $"| {BenchmarkHelpers.EscapeTable(variant)} | {runs.Count} | " +
                $"{FormatBytes(BenchmarkHelpers.Percentile(allocated, 0.5))} | " +
                $"{FormatBytes(allocated.Average())} | " +
                $"{FormatBytes(allocated[^1])} | " +
                $"{FormatCount(BenchmarkHelpers.Percentile(gen0, 0.5))} | " +
                $"{FormatCount(gen0.Average())} | " +
                $"{FormatCount(BenchmarkHelpers.Percentile(gen1, 0.5))} | " +
                $"{FormatCount(gen1.Average())} | " +
                $"{FormatCount(BenchmarkHelpers.Percentile(gen2, 0.5))} | " +
                $"{FormatCount(gen2.Average())} | " +
                $"{FormatSignedBytes(heapDeltas.Count == 0 ? double.NaN : BenchmarkHelpers.Percentile(heapDeltas, 0.5))} | " +
                $"{FormatSignedBytes(fragmentedDeltas.Count == 0 ? double.NaN : BenchmarkHelpers.Percentile(fragmentedDeltas, 0.5))} |");
        }

        report(null);

        static string FormatBytes(double bytes) =>
            double.IsNaN(bytes) ? "-" : BenchmarkHelpers.FormatSize((long)Math.Round(bytes));

        static string FormatCount(double value) =>
            double.IsNaN(value) ? "-" : value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

        static string FormatSignedBytes(double bytes)
        {
            if (double.IsNaN(bytes))
                return "-";

            var rounded = (long)Math.Round(bytes);
            if (rounded > 0)
                return $"+{BenchmarkHelpers.FormatSize(rounded)}";
            if (rounded < 0)
                return $"-{BenchmarkHelpers.FormatSize(Math.Abs(rounded))}";
            return "0 B";
        }
    }

    private enum BandSelector
    {
        Fast,
        Slow,
    }

    private static string FormatConvergence(
        BenchmarkConvergence.Status status,
        double spreadPercent,
        int successfulRuns,
        int desiredRunCount)
    {
        var spread = double.IsNaN(spreadPercent) ? "n/a" : $"{spreadPercent:0.0}%";
        return status switch
        {
            BenchmarkConvergence.Status.Converged => $"converged @ {spread}",
            BenchmarkConvergence.Status.GaveUp => $"⚠ gave up @ {spread}",
            _ => successfulRuns < desiredRunCount
                ? $"⚠ insufficient ({successfulRuns}/{desiredRunCount})"
                : $"⚠ not converged @ {spread}",
        };
    }

    private static void ReportBucketRecommendations(
        Action<string?> report,
        IReadOnlyList<BenchmarkFileCopyRecord> records,
        IReadOnlyList<FileSizeBucket> buckets,
        IReadOnlyList<string> variants,
        List<string> warnings,
        IReadOnlyDictionary<string, string> matchedControls,
        double gatePercent)
    {
        report("### Bucket Strategy Evidence");
        report(null);

        if (records.Count == 0)
        {
            report("No file-level records available. Bucket strategy recommendations cannot be produced.");
            report(null);
            return;
        }

        report("| Bucket | Best Observed Variant | Matched Control | Run-Median MiB/s | Control MiB/s | Delta | Noise Floor | Median File Duration | Verdict | Recommendation |");
        report("|---|---|---|---:|---:|---:|---:|---:|---|---|");

        foreach (var bucket in buckets)
        {
            var candidates = variants
                .Select(v => BenchmarkStatistics.BuildBucketEvidence(records, bucket, v))
                .Where(e => e.RecordCount > 0)
                .OrderByDescending(e => e.AggregateThroughputMiBPerSecond)
                .ToList();

            if (candidates.Count == 0)
                continue;

            var best = candidates[0];
            var controlName = FindMatchedControlVariant(best.VariantName, variants, matchedControls);
            BucketVariantEvidence? control = null;
            if (controlName is not null)
            {
                control = BenchmarkStatistics.BuildBucketEvidence(records, bucket, controlName);
                if (control.RecordCount == 0)
                {
                    warnings.Add($"`{best.VariantName}` in `{bucket.Label}` has matched control `{controlName}`, but that control has no file-level records in the bucket.");
                    control = null;
                }
            }

            var isControl = !matchedControls.ContainsKey(best.VariantName);
            var comparison = BenchmarkComparison.CompareBucketEvidence(best, control, gatePercent, isControl);
            var recommendation = comparison.Verdict == "PASS"
                ? "Candidate for policy"
                : isControl
                    ? "Keep control"
                    : "No supported change";

            report(
                $"| {bucket.Label} | {BenchmarkHelpers.EscapeTable(best.VariantName)} | {BenchmarkHelpers.EscapeTable(controlName ?? "-")} | " +
                $"{best.AggregateThroughputMiBPerSecond:0.00} | {(control is null ? "-" : $"{control.AggregateThroughputMiBPerSecond:0.00}")} | " +
                $"{comparison.DeltaText} | {comparison.NoiseText} | {best.MedianDurationMilliseconds:0.###} ms | " +
                $"{comparison.Verdict} | {recommendation} |");
        }

        report(null);
    }

    private static void ReportBucketMetrics(
        Action<string?> report,
        IReadOnlyList<BenchmarkFileCopyRecord> records,
        IReadOnlyList<FileSizeBucket> buckets,
        IReadOnlyList<string> variants)
    {
        report("### Bucket Metrics");
        report(null);

        if (records.Count == 0)
        {
            report("No file-level records available.");
            report(null);
            return;
        }

        // Per-variant total copy time (all buckets) — the denominator for each bucket's % of copy time.
        // The shares partition the variant's summed per-file durations, which (with batched attribution
        // conserved) ≈ the copy-phase wall clock — so this shows where the run's time actually goes.
        var variantTotalCopyMs = variants.ToDictionary(
            v => v,
            v => BenchmarkStatistics.BuildBucketEvidence(records, null, v).TotalCopyDurationMilliseconds);

        report("| Bucket | Variant | Records | Bytes | % Copy Time | Median Duration | P95 Duration | Run-Median Spread | Aggregate MiB/s |");
        report("|---|---|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var bucket in buckets)
        {
            foreach (var variant in variants)
            {
                var evidence = BenchmarkStatistics.BuildBucketEvidence(records, bucket, variant);
                if (evidence.RecordCount == 0)
                    continue;

                var totalMs = variantTotalCopyMs.GetValueOrDefault(variant);
                var copyTimeShare = totalMs > 0 ? evidence.TotalCopyDurationMilliseconds / totalMs * 100.0 : 0.0;

                report(
                    $"| {bucket.Label} | {BenchmarkHelpers.EscapeTable(variant)} | {evidence.RecordCount} | {BenchmarkHelpers.FormatBytesHuman(evidence.TotalBytes)} | " +
                    $"{copyTimeShare:0.0}% | " +
                    $"{evidence.MedianDurationMilliseconds:0.###} ms | {evidence.P95DurationMilliseconds:0.###} ms | " +
                    $"{evidence.RunMedianSpreadMilliseconds:0.###} ms |" +
                    $"{evidence.AggregateThroughputMiBPerSecond:0.00} | ");
            }
        }

        report(null);
    }

    private static void ReportBatchingIsolationEvidence(
        Action<string?> report,
        IReadOnlyList<BenchmarkFileCopyRecord> records,
        IReadOnlyList<FileSizeBucket> buckets,
        IReadOnlyList<string> variants,
        double gatePercent)
    {
        // variants is already in config order; filter in-place to preserve it
        var directBatchVariants = variants
            .Where(v => v.Contains("DirectWriteBatch", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var unbatchedControl = variants.FirstOrDefault(v =>
            v.Contains("UnbatchedDirectWrite", StringComparison.OrdinalIgnoreCase));

        if (directBatchVariants.Count == 0 || unbatchedControl is null)
            return;

        report("### Batching Isolation Evidence");
        report(null);
        report($"Compares each `DirectWriteBatch*` variant against `{unbatchedControl}` to isolate the contribution of batching beyond direct write alone (Section 7.2.2).");
        report(null);
        report("| Bucket | Variant | Unbatched Control | Run-Median MiB/s | Control MiB/s | Delta | Noise Floor | Verdict |");
        report("|---|---|---|---:|---:|---:|---:|---|");

        foreach (var bucket in buckets)
        {
            var control = BenchmarkStatistics.BuildBucketEvidence(records, bucket, unbatchedControl);
            if (control.RecordCount == 0)
                continue;

            foreach (var variant in directBatchVariants)
            {
                var candidate = BenchmarkStatistics.BuildBucketEvidence(records, bucket, variant);
                if (candidate.RecordCount == 0)
                    continue;

                var comparison = BenchmarkComparison.CompareBucketEvidence(candidate, control, gatePercent, isControl: false);
                report(
                    $"| {bucket.Label} | {BenchmarkHelpers.EscapeTable(variant)} | {BenchmarkHelpers.EscapeTable(unbatchedControl)} | " +
                    $"{candidate.AggregateThroughputMiBPerSecond:0.00} | {control.AggregateThroughputMiBPerSecond:0.00} | " +
                    $"{comparison.DeltaText} | {comparison.NoiseText} | {comparison.Verdict} |");
            }
        }

        report(null);
    }

    private static List<string> BuildMissingControlWarnings(
        IReadOnlyList<string> variants,
        IReadOnlyDictionary<string, string> matchedControls)
    {
        var warnings = new List<string>();
        foreach (var variant in variants)
        {
            // Control variants (no matched control) are the reference — skip them.
            if (!matchedControls.ContainsKey(variant))
                continue;

            var control = FindMatchedControlVariant(variant, variants, matchedControls);
            if (control is null)
                warnings.Add($"`{variant}` has no matched control; causal effect cannot be isolated.");
        }

        return warnings
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Returns the matched control variant name for <paramref name="variant"/> if it appears
    /// in <paramref name="variants"/>. Returns <c>null</c> for control variants (no entry in
    /// <paramref name="matchedControls"/>) or when the control is not present in the scenario.
    /// </summary>
    private static string? FindMatchedControlVariant(
        string variant,
        IReadOnlyList<string> variants,
        IReadOnlyDictionary<string, string> matchedControls)
    {
        if (!matchedControls.TryGetValue(variant, out var controlName) || string.IsNullOrWhiteSpace(controlName))
            return null;
        return variants.FirstOrDefault(v => string.Equals(v, controlName, StringComparison.OrdinalIgnoreCase));
    }

    private static void ReportCrossScenarioSummary(
        Action<string?> report,
        BenchmarkConfig config,
        IReadOnlyList<BenchmarkRunRecord> runs,
        IReadOnlyList<BenchmarkFileCopyRecord> records,
        IReadOnlyList<FileSizeBucket> buckets,
        IReadOnlyList<string> variants,
        IReadOnlyList<string> scenarios)
    {
        // runs and records are already filtered to converged windows per scenario/variant.
        static IReadOnlyList<BenchmarkRunRecord> ScenarioVariantRuns(
            IReadOnlyList<BenchmarkRunRecord> allRuns, string scenario, string variant) =>
            allRuns.Where(r =>
                string.Equals(r.ScenarioName, scenario, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase)).ToList();

        static IReadOnlyList<BenchmarkFileCopyRecord> ScenarioVariantRecords(
            IReadOnlyList<BenchmarkFileCopyRecord> allRecords, string scenario, string variant) =>
            allRecords.Where(r =>
                string.Equals(r.ScenarioName, scenario, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase)).ToList();

        report("## All Scenarios Summary");
        report(null);
        report("Cross-scenario summary matrix displaying median execute durations (for entire runs) and run-median aggregate throughput (MiB/s) per bucket.");
        report(null);

        report("### Run-Level Median Duration");
        report(null);

        var header = "| Variant | " + string.Join(" | ", scenarios.Select(BenchmarkHelpers.EscapeTable)) + " |";
        var divider = "|---|" + string.Join("|", scenarios.Select(_ => "---:")) + "|";
        report(header);
        report(divider);

        foreach (var variant in variants)
        {
            var row = new List<string> { BenchmarkHelpers.EscapeTable(variant) };
            var variantConfig = config.Variants.FirstOrDefault(v => string.Equals(v.Name, variant, StringComparison.OrdinalIgnoreCase));
            var matchedControlName = variantConfig?.MatchedControl;

            foreach (var scenario in scenarios)
            {
                var evidence = BenchmarkStatistics.BuildRunEvidence(ScenarioVariantRuns(runs, scenario, variant), variant);
                if (evidence.TotalRuns == 0)
                {
                    row.Add("-");
                }
                else if (string.IsNullOrEmpty(matchedControlName))
                {
                    row.Add(BenchmarkHelpers.FormatDurationHuman(evidence.MedianSeconds));
                }
                else
                {
                    var controlEvidence = BenchmarkStatistics.BuildRunEvidence(ScenarioVariantRuns(runs, scenario, matchedControlName), matchedControlName);
                    if (controlEvidence.TotalRuns > 0 && controlEvidence.MedianSeconds > 0)
                    {
                        var deltaPercent = (evidence.MedianSeconds - controlEvidence.MedianSeconds) / controlEvidence.MedianSeconds * 100.0;
                        var sign = deltaPercent > 0 ? "+" : "";
                        row.Add($"{BenchmarkHelpers.FormatDurationHuman(evidence.MedianSeconds)} ({sign}{deltaPercent:0.0}%)");
                    }
                    else
                    {
                        row.Add(BenchmarkHelpers.FormatDurationHuman(evidence.MedianSeconds));
                    }
                }
            }

            if (row.Count > 1 && row.Skip(1).Any(c => c != "-"))
                report("| " + string.Join(" | ", row) + " |");
        }
        report(null);

        report("### Scenario-Level Median Throughput (MiB/s)");
        report(null);
        report(header);
        report(divider);

        foreach (var variant in variants)
        {
            var row = new List<string> { BenchmarkHelpers.EscapeTable(variant) };
            var variantConfig = config.Variants.FirstOrDefault(v => string.Equals(v.Name, variant, StringComparison.OrdinalIgnoreCase));
            var matchedControlName = variantConfig?.MatchedControl;

            foreach (var scenario in scenarios)
            {
                var evidence = BenchmarkStatistics.BuildBucketEvidence(ScenarioVariantRecords(records, scenario, variant), null, variant);
                if (evidence.RecordCount == 0)
                {
                    row.Add("-");
                }
                else if (string.IsNullOrEmpty(matchedControlName))
                {
                    row.Add($"{evidence.AggregateThroughputMiBPerSecond:0.00}");
                }
                else
                {
                    var controlEvidence = BenchmarkStatistics.BuildBucketEvidence(ScenarioVariantRecords(records, scenario, matchedControlName), null, matchedControlName);
                    if (controlEvidence.RecordCount > 0 && controlEvidence.AggregateThroughputMiBPerSecond > 0)
                    {
                        var deltaPercent = (evidence.AggregateThroughputMiBPerSecond - controlEvidence.AggregateThroughputMiBPerSecond) / controlEvidence.AggregateThroughputMiBPerSecond * 100.0;
                        var sign = deltaPercent > 0 ? "+" : "";
                        row.Add($"{evidence.AggregateThroughputMiBPerSecond:0.00} ({sign}{deltaPercent:0.0}%)");
                    }
                    else
                    {
                        row.Add($"{evidence.AggregateThroughputMiBPerSecond:0.00}");
                    }
                }
            }

            if (row.Count > 1 && row.Skip(1).Any(c => c != "-"))
                report("| " + string.Join(" | ", row) + " |");
        }
        report(null);

        foreach (var bucket in buckets)
        {
            var hasAnyDataForBucket = records.Any(r => bucket.Contains(r.FileSizeBytes));
            if (!hasAnyDataForBucket) continue;

            report($"### Bucket Throughput (MiB/s): {bucket.Label}");
            report(null);
            report(header);
            report(divider);

            foreach (var variant in variants)
            {
                var row = new List<string> { BenchmarkHelpers.EscapeTable(variant) };
                var variantConfig = config.Variants.FirstOrDefault(v => string.Equals(v.Name, variant, StringComparison.OrdinalIgnoreCase));
                var matchedControlName = variantConfig?.MatchedControl;

                foreach (var scenario in scenarios)
                {
                    var evidence = BenchmarkStatistics.BuildBucketEvidence(ScenarioVariantRecords(records, scenario, variant), bucket, variant);
                    if (evidence.RecordCount == 0)
                    {
                        row.Add("-");
                    }
                    else if (string.IsNullOrEmpty(matchedControlName))
                    {
                        row.Add($"{evidence.AggregateThroughputMiBPerSecond:0.00}");
                    }
                    else
                    {
                        var controlEvidence = BenchmarkStatistics.BuildBucketEvidence(ScenarioVariantRecords(records, scenario, matchedControlName), bucket, matchedControlName);
                        if (controlEvidence.RecordCount > 0 && controlEvidence.AggregateThroughputMiBPerSecond > 0)
                        {
                            var deltaPercent = (evidence.AggregateThroughputMiBPerSecond - controlEvidence.AggregateThroughputMiBPerSecond) / controlEvidence.AggregateThroughputMiBPerSecond * 100.0;
                            var sign = deltaPercent > 0 ? "+" : "";
                            row.Add($"{evidence.AggregateThroughputMiBPerSecond:0.00} ({sign}{deltaPercent:0.0}%)");
                        }
                        else
                        {
                            row.Add($"{evidence.AggregateThroughputMiBPerSecond:0.00}");
                        }
                    }
                }

                if (row.Count > 1 && row.Skip(1).Any(c => c != "-"))
                    report("| " + string.Join(" | ", row) + " |");
            }
            report(null);
        }
    }
}
