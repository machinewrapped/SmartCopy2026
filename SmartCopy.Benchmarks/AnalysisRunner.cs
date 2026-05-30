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

        if (filteredRecords.Count == 0 && filteredRuns.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(selection.ScenarioName))
            {
                Report($"No records found for scenario '{selection.ScenarioName.Trim()}'.");
            }
            else
            {
                Report("No records found for the selected scenarios.");
            }

            if (!string.IsNullOrWhiteSpace(selection.VariantName))
            {
                Report($"Variant filter: '{selection.VariantName}'.");
            }

            await FlushReportAsync();
            return;
        }

        var allVariants = filteredRecords
            .Select(r => r.VariantName)
            .Concat(filteredRuns.Select(r => r.VariantName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, new VariantNameComparer())
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
        Report("- **Verdicts:** `PASS` means the measured improvement exceeds both the gate and observed variance. `INCONCLUSIVE` means the delta is inside variance or a matched control is missing.");
        Report();

        var buckets = config.DatasetPreparation?.Buckets?.Select(b => new FileSizeBucket(b.MinimumFileSizeBytes, b.MaximumFileSizeBytes, b.Name)).ToList()
                      ?? FileSizeBuckets.All.ToList();

        var matchedControlLookup = config.Variants
            .Where(v => !string.IsNullOrWhiteSpace(v.MatchedControl))
            .ToDictionary(v => v.Name, v => v.MatchedControl!, StringComparer.OrdinalIgnoreCase);

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
                .OrderBy(v => v, new VariantNameComparer())
                .ToList();

            Report($"- **Run records:** `{runs.Count}`");
            Report($"- **File records:** `{records.Count}`");
            Report($"- **Variants:** {string.Join(", ", variants.Select(v => $"`{v}`"))}");
            Report();

            var warnings = BuildMissingControlWarnings(variants, matchedControlLookup);

            ReportRunEvidence(Report, runs, variants);
            ReportBucketRecommendations(Report, records, buckets, variants, warnings, matchedControlLookup);
            ReportBucketMetrics(Report, records, buckets, variants);
            ReportBatchingIsolationEvidence(Report, records, buckets, variants);

            if (warnings.Count > 0)
            {
                Report("### Missing Matched Controls");
                foreach (var warning in warnings)
                {
                    Report($"- {warning}");
                }
                Report();
            }

            Report();

            var htmlPath = Path.ChangeExtension(analysisPath, ".html");
            if (scenariosToAnalyze.Count > 1)
            {
                htmlPath = Path.Combine(Path.GetDirectoryName(htmlPath) ?? "", $"{Path.GetFileNameWithoutExtension(htmlPath)}-{scenarioName}.html");
            }
            
            await BenchmarkHtmlReportGenerator.GenerateAsync(htmlPath, scenarioName, buckets, variants, records, runs);
        }

        await FlushReportAsync();
        Console.WriteLine($"Analysis: {analysisPath}");
    }

    private static void ReportRunEvidence(
        Action<string?> report,
        IReadOnlyList<BenchmarkRunRecord> runs,
        IReadOnlyList<string> variants)
    {
        report("### Run-Level Evidence");
        report(null);

        if (runs.Count == 0)
        {
            report("No run-level records available. Whole-policy wall-clock verdicts cannot be produced.");
            report(null);
            return;
        }

        var baselineVariant = FindBaselineVariant(variants);
        var baselineEvidence = baselineVariant is null
            ? null
            : BuildRunEvidence(runs, baselineVariant);

        report("| Variant | Valid Runs | Invalid Runs | Median Execute | Mean Execute | Min | Max | Spread | Delta vs Control | Noise Floor | Verdict |");
        report("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|");

        foreach (var variant in variants)
        {
            var evidence = BuildRunEvidence(runs, variant);
            if (evidence.TotalRuns == 0)
            {
                continue;
            }

            var control = baselineVariant is not null &&
                          !string.Equals(variant, baselineVariant, StringComparison.OrdinalIgnoreCase)
                ? baselineEvidence
                : null;
            var comparison = CompareRunEvidence(evidence, control);

            report(
                $"| {BenchmarkHelpers.EscapeTable(variant)} | {evidence.ValidRuns} | {evidence.InvalidRuns} | " +
                $"{BenchmarkHelpers.FormatDurationHuman(evidence.MedianSeconds)} | {BenchmarkHelpers.FormatDurationHuman(evidence.MeanSeconds)} | " +
                $"{BenchmarkHelpers.FormatDurationHuman(evidence.MinSeconds)} | {BenchmarkHelpers.FormatDurationHuman(evidence.MaxSeconds)} | " +
                $"{BenchmarkHelpers.FormatDurationHuman(evidence.SpreadSeconds)} | {comparison.DeltaText} | {comparison.NoiseText} | {comparison.Verdict} |");
        }

        if (baselineVariant is null)
        {
            report(null);
            report("Run-level verdicts are `INCONCLUSIVE`: no baseline/control variant was found.");
        }

        report(null);
    }

    private static void ReportBucketRecommendations(
        Action<string?> report,
        IReadOnlyList<BenchmarkFileCopyRecord> records,
        IReadOnlyList<FileSizeBucket> buckets,
        IReadOnlyList<string> variants,
        List<string> warnings,
        IReadOnlyDictionary<string, string> matchedControls)
    {
        report("### Bucket Strategy Evidence");
        report(null);

        if (records.Count == 0)
        {
            report("No file-level records available. Bucket strategy recommendations cannot be produced.");
            report(null);
            return;
        }

        report("| Bucket | Best Observed Variant | Matched Control | Median File Duration | Control Median | Delta | Noise Floor | Aggregate MiB/s | Verdict | Recommendation |");
        report("|---|---|---|---:|---:|---:|---:|---:|---|---|");

        foreach (var bucket in buckets)
        {
            var candidates = variants
                .Select(v => BuildBucketEvidence(records, bucket, v))
                .Where(e => e.RecordCount > 0)
                .OrderBy(e => e.MedianDurationMilliseconds)
                .ToList();

            if (candidates.Count == 0)
            {
                continue;
            }

            var best = candidates[0];
            var controlName = FindMatchedControlVariant(best.VariantName, variants, matchedControls);
            BucketVariantEvidence? control = null;
            if (controlName is not null)
            {
                control = BuildBucketEvidence(records, bucket, controlName);
                if (control.RecordCount == 0)
                {
                    warnings.Add($"`{best.VariantName}` in `{bucket.Label}` has matched control `{controlName}`, but that control has no file-level records in the bucket.");
                    control = null;
                }
            }

            var comparison = CompareBucketEvidence(best, control);
            var recommendation = comparison.Verdict == "PASS"
                ? "Candidate for policy"
                : IsBaselineVariant(best.VariantName)
                    ? "Keep control"
                    : "No supported change";

            report(
                $"| {bucket.Label} | {BenchmarkHelpers.EscapeTable(best.VariantName)} | {BenchmarkHelpers.EscapeTable(controlName ?? "-")} | " +
                $"{best.MedianDurationMilliseconds:0.###} ms | {(control is null ? "-" : $"{control.MedianDurationMilliseconds:0.###} ms")} | " +
                $"{comparison.DeltaText} | {comparison.NoiseText} | {best.AggregateThroughputMiBPerSecond:0.00} | " +
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

        report("| Bucket | Variant | Records | Bytes | Median Duration | P95 Duration | Aggregate MiB/s | Mean MiB/s | P50 MiB/s | P95 MiB/s | Run-Median Spread |");
        report("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (var bucket in buckets)
        {
            foreach (var variant in variants)
            {
                var evidence = BuildBucketEvidence(records, bucket, variant);
                if (evidence.RecordCount == 0)
                {
                    continue;
                }

                report(
                    $"| {bucket.Label} | {BenchmarkHelpers.EscapeTable(variant)} | {evidence.RecordCount} | {BenchmarkHelpers.FormatBytesHuman(evidence.TotalBytes)} | " +
                    $"{evidence.MedianDurationMilliseconds:0.###} ms | {evidence.P95DurationMilliseconds:0.###} ms | " +
                    $"{evidence.AggregateThroughputMiBPerSecond:0.00} | {evidence.MeanThroughputMiBPerSecond:0.00} | " +
                    $"{evidence.P50ThroughputMiBPerSecond:0.00} | {evidence.P95ThroughputMiBPerSecond:0.00} | " +
                    $"{evidence.RunMedianSpreadMilliseconds:0.###} ms |");
            }
        }

        report(null);
    }

    private static void ReportBatchingIsolationEvidence(
        Action<string?> report,
        IReadOnlyList<BenchmarkFileCopyRecord> records,
        IReadOnlyList<FileSizeBucket> buckets,
        IReadOnlyList<string> variants)
    {
        var directBatchVariants = variants
            .Where(v => v.Contains("DirectWriteBatch", StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v, new VariantNameComparer())
            .ToList();

        var unbatchedControl = variants.FirstOrDefault(v =>
            v.Contains("UnbatchedDirectWrite", StringComparison.OrdinalIgnoreCase));

        if (directBatchVariants.Count == 0 || unbatchedControl is null)
        {
            return;
        }

        report("### Batching Isolation Evidence");
        report(null);
        report($"Compares each `DirectWriteBatch*` variant against `{unbatchedControl}` to isolate the contribution of batching beyond direct write alone (Section 7.2.2).");
        report(null);
        report("| Bucket | Variant | Unbatched Control | Median Duration | Control Median | Delta | Noise Floor | Verdict |");
        report("|---|---|---|---:|---:|---:|---:|---|");

        foreach (var bucket in buckets)
        {
            var control = BuildBucketEvidence(records, bucket, unbatchedControl);
            if (control.RecordCount == 0)
            {
                continue;
            }

            foreach (var variant in directBatchVariants)
            {
                var candidate = BuildBucketEvidence(records, bucket, variant);
                if (candidate.RecordCount == 0)
                {
                    continue;
                }

                var comparison = CompareBucketEvidence(candidate, control);
                report(
                    $"| {bucket.Label} | {BenchmarkHelpers.EscapeTable(variant)} | {BenchmarkHelpers.EscapeTable(unbatchedControl)} | " +
                    $"{candidate.MedianDurationMilliseconds:0.###} ms | {control.MedianDurationMilliseconds:0.###} ms | " +
                    $"{comparison.DeltaText} | {comparison.NoiseText} | {comparison.Verdict} |");
            }
        }

        report(null);
    }

    private static List<string> BuildMissingControlWarnings(IReadOnlyList<string> variants, IReadOnlyDictionary<string, string> matchedControls)
    {
        var warnings = new List<string>();
        foreach (var variant in variants)
        {
            if (IsBaselineVariant(variant))
            {
                continue;
            }

            var control = FindMatchedControlVariant(variant, variants, matchedControls);
            if (control is null)
            {
                warnings.Add($"`{variant}` has no matched control; causal effect cannot be isolated.");
            }

            if (variant.Contains("DirectWriteBatch", StringComparison.OrdinalIgnoreCase) &&
                !variants.Any(v => v.Contains("UnbatchedDirectWrite", StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add($"`{variant}` has no `UnbatchedDirectWrite*` control; batching cannot be isolated from direct write alone.");
            }
        }

        return warnings
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(w => w, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static RunVariantEvidence BuildRunEvidence(IReadOnlyList<BenchmarkRunRecord> runs, string variant)
    {
        var variantRuns = runs
            .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var validDurations = variantRuns
            .Where(IsSuccessfulRun)
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
                0,
                0,
                0,
                0,
                0);
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
            max - min);
    }

    private static BucketVariantEvidence BuildBucketEvidence(
        IReadOnlyList<BenchmarkFileCopyRecord> records,
        FileSizeBucket bucket,
        string variant)
    {
        var bucketRecords = records
            .Where(r => string.Equals(r.VariantName, variant, StringComparison.OrdinalIgnoreCase))
            .Where(r => bucket.Contains(r.FileSizeBytes))
            .ToList();

        if (bucketRecords.Count == 0)
        {
            return BucketVariantEvidence.Empty(bucket.Label, variant);
        }

        var durations = bucketRecords
            .Select(r => r.CopyDurationMilliseconds)
            .Where(v => v > 0)
            .OrderBy(v => v)
            .ToList();
        var throughputs = bucketRecords
            .Select(r => r.ThroughputMiBPerSecond)
            .Where(v => v is not null)
            .Select(v => v!.Value)
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

        var totalBytes = bucketRecords.Sum(r => r.FileSizeBytes);
        var totalSeconds = bucketRecords.Sum(r => r.CopyDurationMilliseconds) / 1000.0;
        var aggregateThroughput = totalBytes > 0 && totalSeconds > 0
            ? (totalBytes / 1048576.0) / totalSeconds
            : 0.0;

        return new BucketVariantEvidence(
            bucket.Label,
            variant,
            bucketRecords.Count,
            totalBytes,
            durations.Count > 0 ? durations.Average() : 0.0,
            durations.Count > 0 ? BenchmarkHelpers.Percentile(durations, 0.50) : 0.0,
            durations.Count > 0 ? BenchmarkHelpers.Percentile(durations, 0.95) : 0.0,
            aggregateThroughput,
            throughputs.Count > 0 ? throughputs.Average() : 0.0,
            throughputs.Count > 0 ? BenchmarkHelpers.Percentile(throughputs, 0.50) : 0.0,
            throughputs.Count > 0 ? BenchmarkHelpers.Percentile(throughputs, 0.95) : 0.0,
            runMedians.Count >= 2 ? runMedians[^1] - runMedians[0] : 0.0);
    }

    private static EvidenceComparison CompareRunEvidence(RunVariantEvidence candidate, RunVariantEvidence? control)
    {
        if (candidate.ValidRuns == 0)
        {
            return new EvidenceComparison("INVALID", "-", "-");
        }

        if (control is null)
        {
            return IsBaselineVariant(candidate.VariantName)
                ? new EvidenceComparison("CONTROL", "-", "-")
                : new EvidenceComparison("INCONCLUSIVE", "-", "-");
        }

        if (control.ValidRuns == 0)
        {
            return new EvidenceComparison("INCONCLUSIVE", "-", "-");
        }

        var deltaSeconds = control.MedianSeconds - candidate.MedianSeconds;
        var deltaPercent = control.MedianSeconds > 0 ? deltaSeconds / control.MedianSeconds * 100.0 : 0.0;
        var noiseFloor = Math.Max(control.SpreadSeconds, candidate.SpreadSeconds);
        var verdict = GetDeltaVerdict(deltaSeconds, deltaPercent, noiseFloor, gatePercent: 10.0);
        return new EvidenceComparison(
            verdict,
            $"{FormatSignedPercent(deltaPercent)} ({FormatSignedDurationSeconds(deltaSeconds)})",
            BenchmarkHelpers.FormatDurationHuman(noiseFloor));
    }

    private static EvidenceComparison CompareBucketEvidence(BucketVariantEvidence candidate, BucketVariantEvidence? control)
    {
        if (candidate.RecordCount == 0)
        {
            return new EvidenceComparison("INVALID", "-", "-");
        }

        if (control is null)
        {
            return IsBaselineVariant(candidate.VariantName)
                ? new EvidenceComparison("CONTROL", "-", "-")
                : new EvidenceComparison("INCONCLUSIVE", "-", "-");
        }

        if (control.RecordCount == 0)
        {
            return new EvidenceComparison("INCONCLUSIVE", "-", "-");
        }

        var deltaMilliseconds = control.MedianDurationMilliseconds - candidate.MedianDurationMilliseconds;
        var deltaPercent = control.MedianDurationMilliseconds > 0
            ? deltaMilliseconds / control.MedianDurationMilliseconds * 100.0
            : 0.0;
        var noiseFloor = Math.Max(control.RunMedianSpreadMilliseconds, candidate.RunMedianSpreadMilliseconds);
        var verdict = GetDeltaVerdict(deltaMilliseconds, deltaPercent, noiseFloor, gatePercent: 10.0);
        return new EvidenceComparison(
            verdict,
            $"{FormatSignedPercent(deltaPercent)} ({deltaMilliseconds:+0.###;-0.###;0} ms)",
            $"{noiseFloor:0.###} ms");
    }

    private static string GetDeltaVerdict(double delta, double deltaPercent, double noiseFloor, double gatePercent)
    {
        if (delta < -noiseFloor)
        {
            return "REGRESSION";
        }

        if (delta <= noiseFloor)
        {
            return "INCONCLUSIVE";
        }

        return deltaPercent >= gatePercent ? "PASS" : "FAIL";
    }

    private static string? FindMatchedControlVariant(string variant, IReadOnlyList<string> variants, IReadOnlyDictionary<string, string> matchedControls)
    {
        if (IsBaselineVariant(variant))
        {
            return null;
        }

        if (matchedControls.TryGetValue(variant, out var configuredControl) && !string.IsNullOrWhiteSpace(configuredControl))
        {
            return variants.FirstOrDefault(v => string.Equals(v, configuredControl, StringComparison.OrdinalIgnoreCase));
        }

        if (variant.Contains("DirectWriteBatch", StringComparison.OrdinalIgnoreCase))
        {
            var buffer = ExtractBatchBufferLabel(variant);
            if (buffer is not null)
            {
                var staged = variants.FirstOrDefault(v =>
                    v.Contains("StagedWriteBatch", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ExtractBatchBufferLabel(v), buffer, StringComparison.OrdinalIgnoreCase));
                if (staged is not null)
                {
                    return staged;
                }
            }

            return null;
        }

        return FindBaselineVariant(variants);
    }

    private static string? ExtractBatchBufferLabel(string variant)
    {
        var match = System.Text.RegularExpressions.Regex.Match(variant, @"Batch(?<size>\d+MiB)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["size"].Value : null;
    }

    private static string? FindBaselineVariant(IReadOnlyList<string> variants)
    {
        var preferred = new[] { "Control_BaselineAuto", "BaselineAuto", "ScenarioDefaults" };
        foreach (var name in preferred)
        {
            var match = variants.FirstOrDefault(v => string.Equals(v, name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return variants.FirstOrDefault(IsBaselineVariant);
    }

    private static bool IsBaselineVariant(string variant) =>
        variant.Contains("Baseline", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(variant, "ScenarioDefaults", StringComparison.OrdinalIgnoreCase);

    private static bool IsSuccessfulRun(BenchmarkRunRecord run) =>
        string.Equals(run.RunStatus, BenchmarkRunStatus.Completed, StringComparison.OrdinalIgnoreCase) &&
        run.FailedFiles == 0 &&
        run.ExceptionType is null;

    private static string FormatSignedPercent(double value) =>
        value switch
        {
            > 0 => $"+{value:0.0}%",
            < 0 => $"{value:0.0}%",
            _ => "0.0%",
        };

    private static string FormatSignedDurationSeconds(double seconds) =>
        seconds switch
        {
            > 0 => $"+{BenchmarkHelpers.FormatDurationHuman(seconds)}",
            < 0 => $"-{BenchmarkHelpers.FormatDurationHuman(Math.Abs(seconds))}",
            _ => "0s",
        };
}
