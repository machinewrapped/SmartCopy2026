namespace SmartCopy.Benchmarks;

/// <summary>
/// Fail-fast gate for <c>--mode validation</c>: checks whether a converged scenario has any
/// matched-control candidate whose run-level verdict is REGRESSION or INVALID (the only
/// outcomes that stop the pass). Computed from the same converged window and the same
/// <see cref="BenchmarkComparison.CompareRunEvidence"/> path the report uses, so the live
/// gate and the post-hoc conclusion always agree. Called by <see cref="ValidationPass"/>
/// between scenarios.
/// </summary>
internal static class ValidationGate
{
    /// <summary>
    /// Returns <c>true</c> if any matched-control candidate's run-level verdict is
    /// REGRESSION or INVALID, naming the first such pair in config order.
    /// </summary>
    internal static bool ScenarioFailedGate(
        BenchmarkConfig config,
        IReadOnlyList<BenchmarkRunRecord> scenarioTerminalRuns,
        out string? firstFailVariant,
        out string firstFailVerdict)
    {
        firstFailVariant = null;
        firstFailVerdict = "OK";

        var matchedControls = config.Variants
            .Where(v => !string.IsNullOrWhiteSpace(v.MatchedControl))
            .ToDictionary(v => v.Name, v => v.MatchedControl!, StringComparer.OrdinalIgnoreCase);
        if (matchedControls.Count == 0)
            return false;

        var variantIndex = config.Variants.Select((v, i) => (v.Name, i))
            .ToDictionary(t => t.Name, t => t.i, StringComparer.OrdinalIgnoreCase);
        var variants = scenarioTerminalRuns
            .Select(r => r.VariantName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => variantIndex.GetValueOrDefault(v, int.MaxValue))
            .ToList();

        var convergedIndexes = BenchmarkConvergence.GetConvergedIndexesForVariants(config, scenarioTerminalRuns, variants);
        var convergedRuns = scenarioTerminalRuns
            .Where(r => convergedIndexes.TryGetValue(r.VariantName, out var idx) && idx.Contains(r.RunIndex))
            .ToList();

        foreach (var variant in variants)
        {
            if (!matchedControls.TryGetValue(variant, out var controlName))
                continue;

            var candidate = BenchmarkStatistics.BuildRunEvidence(convergedRuns, variant);
            if (candidate.TotalRuns == 0)
                continue;

            var ce = BenchmarkStatistics.BuildRunEvidence(convergedRuns, controlName);
            var control = ce.TotalRuns > 0 ? ce : null;
            var verdict = BenchmarkComparison.CompareRunEvidence(candidate, control, config.GatePercent, isControl: false).Verdict;
            if (verdict is "REGRESSION" or "INVALID")
            {
                firstFailVariant = variant;
                firstFailVerdict = verdict;
                return true;
            }
        }

        return false;
    }
}
