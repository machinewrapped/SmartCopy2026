using System.Text;

namespace SmartCopy.Benchmarks;

/// <summary>
/// Generates the validation conclusion section — a per-pair verdict table and a rolled-up
/// gate decision (PASS / REGRESSION→STOP / INVALID→ABORT / INCOMPLETE). Re-reads the runs
/// file and re-derives converged windows so it is independent of <see cref="AnalysisRunner"/>'s
/// report loop. Appends the conclusion to the existing analysis report file and prints to console.
/// </summary>
internal static class ValidationConclusionReporter
{
    /// <summary>One matched-control candidate's run-level verdict in a validation scenario.</summary>
    internal sealed record ValidationPairResult(
        string Scenario,
        string Candidate,
        string Control,
        string Role,
        string Verdict,
        string DeltaText,
        string NoiseText);

    public static async Task RunAsync(
        string workingDirectory,
        BenchmarkConfig config,
        BenchmarkCliOptions selection,
        CancellationToken ct)
    {
        var fileNames = FileNamesResolver.GetFileNames(selection.ConfigPath);
        var artifactDirectory = BenchmarkHelpers.ResolveArtifactDirectory(workingDirectory, config.SourcePath, config.ArtifactPath);
        var resultsPath = Path.Combine(artifactDirectory, fileNames.Results);
        var analysisPath = Path.Combine(artifactDirectory, fileNames.Analysis);

        if (!File.Exists(resultsPath))
            return;

        var allRuns = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkRunRecord>(resultsPath, ct);
        var pairs = CollectAllValidationPairs(config, selection, allRuns);

        var reportBuilder = new StringBuilder();
        void Report(string? line = null)
        {
            var text = line ?? string.Empty;
            Console.WriteLine(text);
            reportBuilder.AppendLine(text);
        }

        ReportValidationConclusion(Report, config, selection, pairs);

        // Append to the existing analysis report.
        var existingContent = File.Exists(analysisPath) ? await File.ReadAllTextAsync(analysisPath, ct) : "";
        var sb = new StringBuilder(existingContent);
        if (existingContent.Length > 0 && !existingContent.EndsWith('\n'))
            sb.AppendLine();
        sb.Append(reportBuilder.ToString());
        await File.WriteAllTextAsync(analysisPath, sb.ToString(), ct);
    }

    private static List<ValidationPairResult> CollectAllValidationPairs(
        BenchmarkConfig config,
        BenchmarkCliOptions selection,
        IReadOnlyList<BenchmarkRunRecord> allRuns)
    {
        var scenarioOrder = BenchmarkHelpers.BuildScenarioOrder(config, config.Scenarios.Where(s => s.Enabled).Select(s => s.Name));
        if (!string.IsNullOrWhiteSpace(selection.ScenarioName))
            scenarioOrder = scenarioOrder.Where(n => string.Equals(n, selection.ScenarioName, StringComparison.OrdinalIgnoreCase)).ToList();

        var matchedControls = config.Variants
            .Where(v => !string.IsNullOrWhiteSpace(v.MatchedControl))
            .ToDictionary(v => v.Name, v => v.MatchedControl!, StringComparer.OrdinalIgnoreCase);

        var variantIndex = config.Variants.Select((v, i) => (v.Name, i))
            .ToDictionary(t => t.Name, t => t.i, StringComparer.OrdinalIgnoreCase);

        var pairs = new List<ValidationPairResult>();
        foreach (var scenarioName in scenarioOrder)
        {
            var runs = allRuns
                .Where(r => string.Equals(r.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase))
                .Where(BenchmarkHelpers.IsTerminalRun)
                .ToList();
            if (runs.Count == 0)
                continue;

            var variants = runs
                .Select(r => r.VariantName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => variantIndex.GetValueOrDefault(v, int.MaxValue))
                .ToList();

            var convergedIndexes = BenchmarkConvergence.GetConvergedIndexesForVariants(config, runs, variants);
            var convergedRuns = runs
                .Where(r => convergedIndexes.TryGetValue(r.VariantName, out var idx) && idx.Contains(r.RunIndex))
                .ToList();

            CollectValidationPairs(pairs, scenarioName, convergedRuns, variants, matchedControls, config);
        }
        return pairs;
    }

    /// <summary>
    /// Captures the run-level verdict of every matched-control candidate in a scenario.
    /// Candidates with no runs yet are left out (surfaced as "pending" by
    /// <see cref="ReportValidationConclusion"/> against the expected pair set).
    /// </summary>
    private static void CollectValidationPairs(
        List<ValidationPairResult> sink,
        string scenarioName,
        IReadOnlyList<BenchmarkRunRecord> convergedRuns,
        IReadOnlyList<string> variants,
        IReadOnlyDictionary<string, string> matchedControls,
        BenchmarkConfig config)
    {
        foreach (var variant in variants)
        {
            if (!matchedControls.TryGetValue(variant, out var controlName))
                continue;

            var candidate = BenchmarkStatistics.BuildRunEvidence(convergedRuns, variant);
            if (candidate.TotalRuns == 0)
                continue;

            RunVariantEvidence? control = null;
            var ce = BenchmarkStatistics.BuildRunEvidence(convergedRuns, controlName);
            if (ce.TotalRuns > 0)
                control = ce;

            var comparison = BenchmarkComparison.CompareRunEvidence(candidate, control, config.GatePercent, isControl: false);
            var role = config.Variants
                .FirstOrDefault(v => string.Equals(v.Name, variant, StringComparison.OrdinalIgnoreCase))?.ValidationRole;

            sink.Add(new ValidationPairResult(
                scenarioName, variant, controlName,
                string.IsNullOrWhiteSpace(role) ? "Comparison" : role.Trim(),
                comparison.Verdict, comparison.DeltaText, comparison.NoiseText));
        }
    }

    /// <summary>
    /// The opinionated bit: rolls the per-pair verdicts into one gate decision. The pass rule is
    /// uniform — a pair fails only on REGRESSION or INVALID; PASS/BELOW_THRESHOLD/INCONCLUSIVE all
    /// clear. <see cref="ValidationPairResult.Role"/> only colours the wording. Walks scenarios in
    /// execution order so the abort recommendation names the first failing pair the fail-fast
    /// ladder would hit.
    /// </summary>
    private static void ReportValidationConclusion(
        Action<string?> report,
        BenchmarkConfig config,
        BenchmarkCliOptions selection,
        IReadOnlyList<ValidationPairResult> pairs)
    {
        report("## Validation Conclusion");
        report(null);

        var scenarioOrder = BenchmarkHelpers.BuildScenarioOrder(config, config.Scenarios.Where(s => s.Enabled).Select(s => s.Name));
        if (!string.IsNullOrWhiteSpace(selection.ScenarioName))
            scenarioOrder = scenarioOrder.Where(n => string.Equals(n, selection.ScenarioName, StringComparison.OrdinalIgnoreCase)).ToList();

        var variantIndex = config.Variants.Select((v, i) => (v.Name, i))
            .ToDictionary(t => t.Name, t => t.i, StringComparer.OrdinalIgnoreCase);

        var found = new HashSet<(string Scenario, string Candidate)>();
        foreach (var p in pairs)
            found.Add((p.Scenario, p.Candidate));

        var pending = new List<(string Scenario, string Candidate)>();
        foreach (var scenarioName in scenarioOrder)
        {
            var scenario = config.Scenarios.FirstOrDefault(s => string.Equals(s.Name, scenarioName, StringComparison.OrdinalIgnoreCase));
            if (scenario is null)
                continue;
            foreach (var variant in config.Variants.Where(v => v.Enabled && !string.IsNullOrWhiteSpace(v.MatchedControl)))
            {
                if (scenario.Variants is { Count: > 0 } && !scenario.Variants.Contains(variant.Name, StringComparer.OrdinalIgnoreCase))
                    continue;
                if (!found.Contains((scenarioName, variant.Name)))
                    pending.Add((scenarioName, variant.Name));
            }
        }

        if (pairs.Count == 0 && pending.Count == 0)
        {
            report("No matched-control pairs configured — nothing to validate. Set `matchedControl` on the candidate variants.");
            report(null);
            return;
        }

        report("| Scenario | Pair | Role | Verdict | Delta vs Control | Noise Floor | Outcome |");
        report("|---|---|---|---|---:|---:|---|");
        foreach (var scenarioName in scenarioOrder)
        {
            foreach (var pair in pairs
                .Where(p => string.Equals(p.Scenario, scenarioName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => variantIndex.GetValueOrDefault(p.Candidate, int.MaxValue)))
            {
                report(
                    $"| {BenchmarkHelpers.EscapeTable(pair.Scenario)} | " +
                    $"{BenchmarkHelpers.EscapeTable($"{pair.Candidate} vs {pair.Control}")} | {pair.Role} | " +
                    $"{pair.Verdict} | {pair.DeltaText} | {pair.NoiseText} | {DescribePairOutcome(pair)} |");
            }
        }
        report(null);

        var ordered = pairs
            .OrderBy(p => scenarioOrder.FindIndex(s => string.Equals(s, p.Scenario, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(p => variantIndex.GetValueOrDefault(p.Candidate, int.MaxValue))
            .ToList();
        var firstInvalid = ordered.FirstOrDefault(p => p.Verdict == "INVALID");
        var firstRegression = ordered.FirstOrDefault(p => p.Verdict == "REGRESSION");
        var valuePasses = pairs.Count(p => string.Equals(p.Role, "Value", StringComparison.OrdinalIgnoreCase) && p.Verdict == "PASS");
        var valuePairs = pairs.Count(p => string.Equals(p.Role, "Value", StringComparison.OrdinalIgnoreCase));

        string verdict, action;
        if (firstInvalid is not null)
        {
            verdict = "INVALID → ABORT";
            action = $"A production copy faulted or produced no valid runs first at `{firstInvalid.Scenario}` " +
                     $"(`{firstInvalid.Candidate}`). Fix the fault before spending further drive-time; the matrix is not trustworthy past this point.";
        }
        else if (firstRegression is not null)
        {
            verdict = "REGRESSION → STOP";
            action = $"`{firstRegression.Candidate}` is slower than `{firstRegression.Control}` beyond noise, first at " +
                     $"`{firstRegression.Scenario}` ({firstRegression.DeltaText}). Chase wrapper overhead " +
                     "(per-file allocation, progress wiring, the `ExistsAsync` pre-check, staging) before any further pairs. Do not promote.";
        }
        else if (pending.Count > 0)
        {
            verdict = "INCOMPLETE";
            var preview = string.Join(", ", pending.Take(6).Select(p => $"`{p.Scenario}`/`{p.Candidate}`"));
            if (pending.Count > 6) preview += $", +{pending.Count - 6} more";
            action = $"No regression or invalid result so far, but {pending.Count} expected pair(s) have not converged yet: {preview}. " +
                     "Re-run validation to continue; a PASS cannot be declared until every pair has a verdict.";
        }
        else
        {
            verdict = "PASS";
            action = valuePairs > 0 && valuePasses > 0
                ? "Every pair cleared (no regression, nothing invalid) and the candidate bundle beats the legacy baseline beyond the gate on at least one pair. Safe to promote — flip `AllowCopyOptimisations` on."
                : "Every pair cleared (no regression, nothing invalid). The candidate bundle is no worse than the legacy baseline, though no gate-clearing gain was measured on this data. Safe to promote on a non-regression basis.";
        }

        report($"**Verdict: {verdict}**");
        report(null);
        report(action);
        report(null);
        Console.WriteLine();
        Console.WriteLine($"[VALIDATION VERDICT] {verdict}");
    }

    /// <summary>Plain-language outcome for one pair; role colours the wording, the pass rule does not change.</summary>
    private static string DescribePairOutcome(ValidationPairResult pair)
    {
        var isEquivalence = string.Equals(pair.Role, "Equivalence", StringComparison.OrdinalIgnoreCase);
        var isValue = string.Equals(pair.Role, "Value", StringComparison.OrdinalIgnoreCase);
        return pair.Verdict switch
        {
            "INVALID" => "FAIL — no valid runs",
            "REGRESSION" => "FAIL — slower than control",
            "PASS" => isValue ? "OK — gain clears gate" : "OK — faster than control",
            "BELOW_THRESHOLD" => "OK — real gain, below gate",
            "INCONCLUSIVE" => isEquivalence ? "OK — parity (within noise)" : "OK — no change (within noise)",
            _ => pair.Verdict,
        };
    }
}
