using System.Text;

namespace SmartCopy.Benchmarks;

internal static class CompareRunner
{
    private const int DiagnosticFileLimit = 12;
    private const double FileDeltaThresholdMilliseconds = 1.0;

    private readonly record struct RunKey(DateTime RunStartedUtc, int RunIndex);

    private sealed record Dataset(
        string Label,
        string Directory,
        IReadOnlyList<BenchmarkRunRecord> Runs,
        IReadOnlyList<BenchmarkFileCopyRecord> FileRecords);

    private sealed record SelectedWindow(
        string Label,
        string Variant,
        IReadOnlyList<BenchmarkRunRecord> Runs,
        IReadOnlyList<BenchmarkFileCopyRecord> FileRecords,
        int AvailableRuns,
        double SpreadPercent,
        bool MetThreshold);

    private sealed record BucketStats(
        string Label,
        int RunCount,
        int RecordCount,
        long BytesPerRun,
        double MedianSeconds,
        double MedianThroughputMiBPerSecond,
        double P10ThroughputMiBPerSecond,
        double P90ThroughputMiBPerSecond);

    private sealed record FileMedian(
        string File,
        long Size,
        double MedianMilliseconds,
        int Samples);

    private sealed record FileDelta(
        string File,
        long Size,
        double ArchiveMilliseconds,
        double CurrentMilliseconds,
        double DeltaMilliseconds,
        double DeltaPercent);

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
        var archiveDirectory = Path.GetFullPath(selection.ComparePath, workingDirectory);
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

        var currentResultsFileName = SelectAvailableFileName(artifactDirectory, fileNames.Results, FileNamesResolver.DefaultResults);
        var archiveResultsFileName = SelectAvailableFileName(archiveDirectory, fileNames.Results, FileNamesResolver.DefaultResults);
        var currentFileResultsFileName = SelectAvailableFileName(artifactDirectory, fileNames.FileResults, FileNamesResolver.DefaultFileResults);
        var archiveFileResultsFileName = SelectAvailableFileName(archiveDirectory, fileNames.FileResults, FileNamesResolver.DefaultFileResults);

        var current = await ReadDatasetAsync("Current", artifactDirectory, currentResultsFileName, currentFileResultsFileName, ct);
        var archive = await ReadDatasetAsync("Archive", archiveDirectory, archiveResultsFileName, archiveFileResultsFileName, ct);

        if (current.Runs.Count == 0 || archive.Runs.Count == 0)
        {
            Report("# Benchmark Comparison Report");
            Report();
            Report("Compare mode requires run-level result files in both directories.");
            Report($"- Current runs: `{Path.Combine(artifactDirectory, currentResultsFileName)}` ({current.Runs.Count})");
            Report($"- Archive runs: `{Path.Combine(archiveDirectory, archiveResultsFileName)}` ({archive.Runs.Count})");
            await FlushReportAsync();
            return;
        }

        if (current.FileRecords.Count == 0 || archive.FileRecords.Count == 0)
        {
            Report("# Benchmark Comparison Report");
            Report();
            Report("Compare mode requires file-level result files in both directories.");
            Report($"- Current file records: `{Path.Combine(artifactDirectory, currentFileResultsFileName)}` ({current.FileRecords.Count})");
            Report($"- Archive file records: `{Path.Combine(archiveDirectory, archiveFileResultsFileName)}` ({archive.FileRecords.Count})");
            await FlushReportAsync();
            return;
        }

        var scenarioOrder = BenchmarkHelpers.BuildScenarioOrder(config, config.Scenarios.Where(s => s.Enabled).Select(s => s.Name));
        var scenariosToAnalyze = !string.IsNullOrWhiteSpace(selection.ScenarioName)
            ? [selection.ScenarioName.Trim()]
            : scenarioOrder;

        if (scenariosToAnalyze.Count == 0)
        {
            scenariosToAnalyze = current.Runs.Select(r => r.ScenarioName)
                .Intersect(archive.Runs.Select(r => r.ScenarioName), StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var buckets = config.DatasetPreparation?.Buckets?.Select(b => new FileSizeBucket(b.MinimumFileSizeBytes, b.MaximumFileSizeBytes, b.Name)).ToList()
                      ?? FileSizeBuckets.All.ToList();

        Report("# Benchmark Comparison Report");
        Report($"- **Current:** `{artifactDirectory}`");
        Report($"- **Archive:** `{archiveDirectory}`");
        Report($"- **Current files:** `{currentResultsFileName}`, `{currentFileResultsFileName}`");
        Report($"- **Archive files:** `{archiveResultsFileName}`, `{archiveFileResultsFileName}`");
        Report($"- **Run records:** current `{current.Runs.Count}`, archive `{archive.Runs.Count}`");
        Report($"- **File records:** current `{current.FileRecords.Count}`, archive `{archive.FileRecords.Count}`");
        Report();
        if (!string.Equals(currentResultsFileName, archiveResultsFileName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(currentFileResultsFileName, archiveFileResultsFileName, StringComparison.OrdinalIgnoreCase))
        {
            Report("> Note: current and archive use different result filenames. This can be valid when comparing equivalent configs across old/new naming schemes; verify the config and scenario metadata before treating it as an apples-to-apples result.");
            Report();
        }

        if (current.Runs.Count < archive.Runs.Count / 4 || current.FileRecords.Count < archive.FileRecords.Count / 4)
        {
            Report("> Note: current has much less data than archive. Window and bucket comparisons may be dominated by an incomplete or aborted current run.");
            Report();
        }

        Report("Window statistics use the same tightest-window idea as validation: the selected window is the tightest configured run count for that variant. The combined view pools archive + current runs before selecting that window, keyed by run start time and run index so duplicate run indexes across sessions do not collide.");
        Report();

        foreach (var scenarioName in scenariosToAnalyze)
        {
            ReportScenario(Report, config, selection, current, archive, scenarioName, buckets);
        }

        await FlushReportAsync();
        Console.WriteLine($"Compare report written to: {reportPath}");
    }

    private static async Task<Dataset> ReadDatasetAsync(
        string label,
        string directory,
        string resultsFileName,
        string fileResultsFileName,
        CancellationToken ct)
    {
        var runsPath = Path.Combine(directory, resultsFileName);
        var fileRecordsPath = Path.Combine(directory, fileResultsFileName);

        var runs = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkRunRecord>(runsPath, ct);
        var fileRecords = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkFileCopyRecord>(fileRecordsPath, ct);
        return new Dataset(label, directory, runs, fileRecords);
    }

    private static string SelectAvailableFileName(
        string directory,
        string preferredFileName,
        string fallbackFileName)
    {
        if (File.Exists(Path.Combine(directory, preferredFileName)))
        {
            return preferredFileName;
        }

        return fallbackFileName;
    }

    private static void ReportScenario(
        Action<string?> report,
        BenchmarkConfig config,
        BenchmarkCliOptions selection,
        Dataset current,
        Dataset archive,
        string scenarioName,
        IReadOnlyList<FileSizeBucket> buckets)
    {
        report($"## Scenario: `{BenchmarkHelpers.EscapeTable(scenarioName)}`");
        report(null);

        var currentRuns = ScenarioRuns(current.Runs, scenarioName);
        var archiveRuns = ScenarioRuns(archive.Runs, scenarioName);
        var currentFiles = ScenarioFiles(current.FileRecords, scenarioName);
        var archiveFiles = ScenarioFiles(archive.FileRecords, scenarioName);
        var combined = new Dataset(
            "Combined",
            $"{archive.Directory} + {current.Directory}",
            [.. archiveRuns, .. currentRuns],
            [.. archiveFiles, .. currentFiles]);

        var variants = currentRuns.Select(r => r.VariantName)
            .Intersect(archiveRuns.Select(r => r.VariantName), StringComparer.OrdinalIgnoreCase)
            .Where(v => string.IsNullOrWhiteSpace(selection.VariantName) || string.Equals(v, selection.VariantName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => VariantOrder(config, v))
            .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (variants.Count == 0)
        {
            report("No common variants to compare.");
            report(null);
            return;
        }

        var windows = variants.ToDictionary(
            v => v,
            v => (
                Archive: SelectWindow("Archive", archiveRuns, archiveFiles, scenarioName, v, config),
                Current: SelectWindow("Current", currentRuns, currentFiles, scenarioName, v, config),
                Combined: SelectWindow("Combined", combined.Runs, combined.FileRecords, scenarioName, v, config)),
            StringComparer.OrdinalIgnoreCase);

        var archiveRunKeys = archiveRuns.Select(ToRunKey).ToHashSet();
        var currentRunKeys = currentRuns.Select(ToRunKey).ToHashSet();

        ReportRunWindowSummary(report, variants, windows, archiveRunKeys, currentRunKeys);
        ReportCombinedMatchedControlEvidence(report, config, variants, windows);

        foreach (var variant in variants)
        {
            var trio = windows[variant];
            report($"### Variant: `{BenchmarkHelpers.EscapeTable(variant)}`");
            report(null);
            ReportBucketComparison(report, buckets, trio.Archive, trio.Current, trio.Combined);
            ReportFileDiagnostics(report, trio.Archive, trio.Current);
            report(null);
        }
    }

    private static List<BenchmarkRunRecord> ScenarioRuns(IReadOnlyList<BenchmarkRunRecord> runs, string scenarioName) =>
        runs
            .Where(r => string.Equals(r.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase))
            .Where(BenchmarkHelpers.IsTerminalRun)
            .ToList();

    private static List<BenchmarkFileCopyRecord> ScenarioFiles(IReadOnlyList<BenchmarkFileCopyRecord> records, string scenarioName) =>
        records
            .Where(r => string.Equals(r.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase))
            .ToList();

    private static SelectedWindow SelectWindow(
        string label,
        IReadOnlyList<BenchmarkRunRecord> scenarioRuns,
        IReadOnlyList<BenchmarkFileCopyRecord> scenarioFiles,
        string scenarioName,
        string variant,
        BenchmarkConfig config)
    {
        var successful = scenarioRuns
            .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
            .Where(BenchmarkHelpers.IsSuccessfulRun)
            .ToList();

        if (successful.Count == 0)
            return new SelectedWindow(label, variant, [], [], 0, double.NaN, false);

        var desiredRunCount = BenchmarkConvergence.GetDesiredRunCount(config, variant);
        var selectedRuns = FindTightestWindow(successful, desiredRunCount, config.ConvergenceSpreadPercent, out var spread, out var metThreshold);
        var selectedKeys = selectedRuns.Select(ToRunKey).ToHashSet();
        var selectedFiles = scenarioFiles
            .Where(r => string.Equals(r.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase))
            .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
            .Where(r => selectedKeys.Contains(new RunKey(r.RunStartedUtc, r.RunIndex)))
            .ToList();

        return new SelectedWindow(label, variant, selectedRuns, selectedFiles, successful.Count, spread, metThreshold);
    }

    private static List<BenchmarkRunRecord> FindTightestWindow(
        IReadOnlyList<BenchmarkRunRecord> successfulRuns,
        int desiredRunCount,
        double thresholdPercent,
        out double spreadPercent,
        out bool metThreshold)
    {
        var sorted = successfulRuns
            .OrderBy(r => r.ExecuteDuration.TotalSeconds)
            .ToList();

        var windowSize = Math.Min(desiredRunCount, sorted.Count);
        if (windowSize == sorted.Count)
        {
            spreadPercent = ComputeSpreadPercent(sorted);
            metThreshold = sorted.Count >= desiredRunCount && spreadPercent <= thresholdPercent;
            return sorted;
        }

        List<BenchmarkRunRecord>? bestWindow = null;
        var bestSpread = double.MaxValue;
        metThreshold = false;

        for (var i = 0; i <= sorted.Count - windowSize; i++)
        {
            var candidate = sorted.Skip(i).Take(windowSize).ToList();
            var candidateSpread = ComputeSpreadPercent(candidate);
            if (candidateSpread < bestSpread)
            {
                bestSpread = candidateSpread;
                bestWindow = candidate;
            }

            if (candidateSpread <= thresholdPercent)
            {
                metThreshold = true;
                break;
            }
        }

        spreadPercent = bestSpread == double.MaxValue ? double.NaN : bestSpread;
        return bestWindow ?? sorted.Take(windowSize).ToList();
    }

    private static double ComputeSpreadPercent(IReadOnlyList<BenchmarkRunRecord> runs)
    {
        if (runs.Count < 2)
            return 0.0;

        var durations = runs
            .Select(r => r.ExecuteDuration.TotalSeconds)
            .OrderBy(v => v)
            .ToList();
        var median = BenchmarkHelpers.Percentile(durations, 0.50);
        return median > 0 ? (durations[^1] - durations[0]) / median * 100.0 : 0.0;
    }

    private static void ReportRunWindowSummary(
        Action<string?> report,
        IReadOnlyList<string> variants,
        IReadOnlyDictionary<string, (SelectedWindow Archive, SelectedWindow Current, SelectedWindow Combined)> windows,
        IReadOnlySet<RunKey> archiveRunKeys,
        IReadOnlySet<RunKey> currentRunKeys)
    {
        report("### Run Window Summary");
        report(null);
        report("| Variant | Archive Window | Archive Median | Current Window | Current Median | Current vs Archive | Combined Window | Combined Sources | Combined Median |");
        report("|---|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var variant in variants)
        {
            var trio = windows[variant];
            var archiveMedian = MedianExecuteSeconds(trio.Archive);
            var currentMedian = MedianExecuteSeconds(trio.Current);
            var delta = FormatDeltaPercent(currentMedian, archiveMedian, lowerIsBetter: true);

            report(
                $"| {BenchmarkHelpers.EscapeTable(variant)} | " +
                $"{FormatWindow(trio.Archive)} | {FormatSeconds(archiveMedian)} | " +
                $"{FormatWindow(trio.Current)} | {FormatSeconds(currentMedian)} | {delta} | " +
                $"{FormatWindow(trio.Combined)} | {FormatSourceMix(trio.Combined, archiveRunKeys, currentRunKeys)} | " +
                $"{FormatSeconds(MedianExecuteSeconds(trio.Combined))} |");
        }

        report(null);
        report("A `*` means the selected window is the tightest available window but does not meet the configured convergence spread.");
        report(null);
    }

    private static void ReportCombinedMatchedControlEvidence(
        Action<string?> report,
        BenchmarkConfig config,
        IReadOnlyList<string> variants,
        IReadOnlyDictionary<string, (SelectedWindow Archive, SelectedWindow Current, SelectedWindow Combined)> windows)
    {
        var matchedControls = config.Variants
            .Where(v => v.Enabled && !string.IsNullOrWhiteSpace(v.MatchedControl))
            .ToDictionary(v => v.Name, v => v.MatchedControl!, StringComparer.OrdinalIgnoreCase);

        var candidateVariants = variants
            .Where(v => matchedControls.ContainsKey(v) && variants.Contains(matchedControls[v], StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (candidateVariants.Count == 0)
            return;

        var selectedCombinedRuns = windows.Values
            .SelectMany(w => w.Combined.Runs)
            .ToList();

        report("### Combined Matched-Control Evidence");
        report(null);
        report("| Pair | Verdict | Delta vs Control | Noise Floor |");
        report("|---|---|---:|---:|");

        foreach (var variant in candidateVariants)
        {
            var controlName = matchedControls[variant];
            var candidate = BenchmarkStatistics.BuildRunEvidence(selectedCombinedRuns, variant);
            var controlEvidence = BenchmarkStatistics.BuildRunEvidence(selectedCombinedRuns, controlName);
            var control = controlEvidence.TotalRuns > 0 ? controlEvidence : null;
            var comparison = BenchmarkComparison.CompareRunEvidence(candidate, control, config.GatePercent, isControl: false);
            report(
                $"| {BenchmarkHelpers.EscapeTable($"{variant} vs {controlName}")} | " +
                $"{comparison.Verdict} | {comparison.DeltaText} | {comparison.NoiseText} |");
        }

        report(null);
    }

    private static void ReportBucketComparison(
        Action<string?> report,
        IReadOnlyList<FileSizeBucket> buckets,
        SelectedWindow archive,
        SelectedWindow current,
        SelectedWindow combined)
    {
        report("#### Bucket Time Contribution");
        report(null);
        report("| Bucket | Archive Sec/Run | Current Sec/Run | Delta Sec/Run | Archive MiB/s | Current MiB/s | Delta MiB/s | Combined P10 MiB/s | Combined Median MiB/s | Combined P90 MiB/s |");
        report("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var bucket in buckets)
        {
            var archiveStats = BuildBucketStats(archive.FileRecords, bucket);
            var currentStats = BuildBucketStats(current.FileRecords, bucket);
            var combinedStats = BuildBucketStats(combined.FileRecords, bucket);
            if (archiveStats.RecordCount == 0 && currentStats.RecordCount == 0 && combinedStats.RecordCount == 0)
                continue;

            report(
                $"| {BenchmarkHelpers.EscapeTable(bucket.Label)} | " +
                $"{FormatSeconds(archiveStats.MedianSeconds)} | " +
                $"{FormatSeconds(currentStats.MedianSeconds)} | " +
                $"{FormatSignedSeconds(currentStats.MedianSeconds - archiveStats.MedianSeconds)} | " +
                $"{archiveStats.MedianThroughputMiBPerSecond:0.00} | " +
                $"{currentStats.MedianThroughputMiBPerSecond:0.00} | " +
                $"{FormatDeltaPercent(currentStats.MedianThroughputMiBPerSecond, archiveStats.MedianThroughputMiBPerSecond, lowerIsBetter: false)} | " +
                $"{combinedStats.P10ThroughputMiBPerSecond:0.00} | " +
                $"{combinedStats.MedianThroughputMiBPerSecond:0.00} | " +
                $"{combinedStats.P90ThroughputMiBPerSecond:0.00} |");
        }

        report(null);
        report("Sec/Run and MiB/s are medians of selected per-run bucket aggregates. Combined columns pool archive + current selected runs.");
        report(null);
    }

    private static BucketStats BuildBucketStats(
        IReadOnlyList<BenchmarkFileCopyRecord> selectedRecords,
        FileSizeBucket bucket)
    {
        if (selectedRecords.Count == 0)
            return EmptyBucket(bucket.Label);

        var bucketRuns = selectedRecords
            .Where(r => bucket.Contains(r.FileSizeBytes))
            .GroupBy(r => new RunKey(r.RunStartedUtc, r.RunIndex))
            .Select(g =>
            {
                var bytes = g.Sum(r => r.FileSizeBytes);
                var seconds = g.Sum(r => r.CopyDurationMilliseconds) / 1000.0;
                var throughput = bytes > 0 && seconds > 0 ? (bytes / 1048576.0) / seconds : 0.0;
                return new
                {
                    Records = g.Count(),
                    Bytes = bytes,
                    Seconds = seconds,
                    Throughput = throughput,
                };
            })
            .ToList();

        if (bucketRuns.Count == 0)
            return EmptyBucket(bucket.Label);

        var seconds = bucketRuns.Select(r => r.Seconds).OrderBy(v => v).ToList();
        var throughputs = bucketRuns.Select(r => r.Throughput).OrderBy(v => v).ToList();

        return new BucketStats(
            bucket.Label,
            bucketRuns.Count,
            bucketRuns.Sum(r => r.Records),
            (long)Math.Round(bucketRuns.Select(r => (double)r.Bytes).OrderBy(v => v).ElementAt(bucketRuns.Count / 2)),
            BenchmarkHelpers.Percentile(seconds, 0.50),
            BenchmarkHelpers.Percentile(throughputs, 0.50),
            BenchmarkHelpers.Percentile(throughputs, 0.10),
            BenchmarkHelpers.Percentile(throughputs, 0.90));
    }

    private static BucketStats EmptyBucket(string label) =>
        new(label, 0, 0, 0, 0, 0, 0, 0);

    private static void ReportFileDiagnostics(
        Action<string?> report,
        SelectedWindow archive,
        SelectedWindow current)
    {
        var archiveByFile = BuildFileMedians(archive.FileRecords);
        var currentByFile = BuildFileMedians(current.FileRecords);
        var deltas = currentByFile.Values
            .Where(c => archiveByFile.ContainsKey(c.File))
            .Select(c =>
            {
                var a = archiveByFile[c.File];
                var deltaMs = c.MedianMilliseconds - a.MedianMilliseconds;
                var deltaPct = a.MedianMilliseconds > 0 ? deltaMs / a.MedianMilliseconds * 100.0 : 0.0;
                return new FileDelta(c.File, c.Size, a.MedianMilliseconds, c.MedianMilliseconds, deltaMs, deltaPct);
            })
            .ToList();

        if (deltas.Count == 0)
            return;

        var regressions = deltas.Where(d => d.DeltaMilliseconds >= FileDeltaThresholdMilliseconds).OrderByDescending(d => d.DeltaMilliseconds).ToList();
        var improvements = deltas.Where(d => d.DeltaMilliseconds <= -FileDeltaThresholdMilliseconds).OrderBy(d => d.DeltaMilliseconds).ToList();

        report("#### File-Level Diagnostics");
        report(null);
        report(
            $"Compared `{deltas.Count}` common files in the selected archive/current windows. " +
            $"Files >= {FileDeltaThresholdMilliseconds:0.0} ms slower: `{regressions.Count}`; " +
            $"files >= {FileDeltaThresholdMilliseconds:0.0} ms faster: `{improvements.Count}`. " +
            "These rows are diagnostics only; they are not independent observations.");
        report(null);

        ReportFileDeltaTable(report, "Largest Slower Files", regressions.Take(DiagnosticFileLimit));
        ReportFileDeltaTable(report, "Largest Faster Files", improvements.Take(DiagnosticFileLimit));
    }

    private static Dictionary<string, FileMedian> BuildFileMedians(IReadOnlyList<BenchmarkFileCopyRecord> records) =>
        records
            .GroupBy(r => r.SourceRelativePath, StringComparer.Ordinal)
            .Select(g =>
            {
                var durations = g.Select(r => r.CopyDurationMilliseconds).OrderBy(v => v).ToList();
                return new FileMedian(
                    g.Key,
                    g.First().FileSizeBytes,
                    BenchmarkHelpers.Percentile(durations, 0.50),
                    durations.Count);
            })
            .ToDictionary(x => x.File, StringComparer.Ordinal);

    private static void ReportFileDeltaTable(
        Action<string?> report,
        string title,
        IEnumerable<FileDelta> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0)
            return;

        report($"##### {title} (sorted by absolute delta)");
        report(null);
        report("| File | Delta | Delta % | Archive Median | Current Median | Size |");
        report("|---|---:|---:|---:|---:|---:|");
        foreach (var row in list)
        {
            report(
                $"| `{BenchmarkHelpers.EscapeTable(row.File)}` | " +
                $"{row.DeltaMilliseconds:+0.00;-0.00;0.00} ms | {row.DeltaPercent:+0.0;-0.0;0.0}% | " +
                $"{row.ArchiveMilliseconds:0.00} ms | {row.CurrentMilliseconds:0.00} ms | " +
                $"{BenchmarkHelpers.FormatBytesHuman(row.Size)} |");
        }
        report(null);
    }

    private static RunKey ToRunKey(BenchmarkRunRecord run) => new(run.RunStartedUtc, run.RunIndex);

    private static int VariantOrder(BenchmarkConfig config, string variantName)
    {
        var index = config.Variants.FindIndex(v => string.Equals(v.Name, variantName, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : int.MaxValue;
    }

    private static double MedianExecuteSeconds(SelectedWindow window)
    {
        var durations = window.Runs.Select(r => r.ExecuteDuration.TotalSeconds).OrderBy(v => v).ToList();
        return durations.Count > 0 ? BenchmarkHelpers.Percentile(durations, 0.50) : 0.0;
    }

    private static string FormatWindow(SelectedWindow window)
    {
        if (window.AvailableRuns == 0)
            return "-";

        var marker = window.MetThreshold ? "" : "*";
        return $"{window.Runs.Count}/{window.AvailableRuns} @ {window.SpreadPercent:0.0}%{marker}";
    }

    private static string FormatSourceMix(
        SelectedWindow window,
        IReadOnlySet<RunKey> archiveRunKeys,
        IReadOnlySet<RunKey> currentRunKeys)
    {
        var archiveCount = 0;
        var currentCount = 0;
        foreach (var run in window.Runs)
        {
            var key = ToRunKey(run);
            if (archiveRunKeys.Contains(key))
            {
                archiveCount++;
            }
            else if (currentRunKeys.Contains(key))
            {
                currentCount++;
            }
        }

        return $"{archiveCount} arch / {currentCount} cur";
    }

    private static string FormatSeconds(double seconds) =>
        seconds > 0 ? $"{seconds:0.000}s" : "-";

    private static string FormatSignedSeconds(double seconds) =>
        seconds switch
        {
            > 0 => $"+{seconds:0.000}s",
            < 0 => $"{seconds:0.000}s",
            _ => "0.000s",
        };

    private static string FormatDeltaPercent(double current, double baseline, bool lowerIsBetter)
    {
        if (baseline <= 0 || current <= 0)
            return "-";

        var delta = lowerIsBetter
            ? (baseline - current) / baseline * 100.0
            : (current - baseline) / baseline * 100.0;
        return BenchmarkComparison.FormatSignedPercent(delta);
    }

}
