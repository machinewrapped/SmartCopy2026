namespace SmartCopy.Benchmarks;

/// <summary>
/// Broad-first scheduler for <c>--mode benchmark</c>: interleaves all unconverged scenarios each
/// round, running the lowest-run-count variants of each so they converge together. Validation
/// mode (<see cref="ValidationPass"/>) uses a depth-first, fail-fast scheduler instead.
/// </summary>
internal sealed class BenchmarkPass : BenchmarkPassBase
{
    internal BenchmarkPass(
        string workingDirectory,
        BenchmarkConfig config,
        BenchmarkCliOptions selection,
        SessionPaths paths,
        CancellationToken ct)
        : base(workingDirectory, config, selection, paths, ct)
    {
    }

    protected override List<BenchmarkScenarioGroup> DetermineVariantsToRun(int sessionRoundsCompleted)
    {
        var isExplicit = !string.IsNullOrWhiteSpace(Selection.ScenarioName)
                      || !string.IsNullOrWhiteSpace(Selection.VariantName)
                      || Selection.Runs.HasValue;
        var explicitRuns = Selection.Runs ?? 1;

        if (isExplicit && sessionRoundsCompleted >= explicitRuns)
            return [];

        var groups = BuildScenarioGroups();

        if (isExplicit)
            return groups;

        var unconvergedGroups = groups
            .Select(g => g with { Variants = g.Variants
                .Where(s => BenchmarkConvergence.Check(HistoricalRuns, s.Scenario.Name, s.Variant, Config)
                            == BenchmarkConvergence.Status.NotConverged)
                .ToList() })
            .Where(g => g.Variants.Count > 0)
            .ToList();
        if (unconvergedGroups.Count == 0) return [];
        var minRunCount = unconvergedGroups.SelectMany(g => g.Variants).Min(s => s.SuccessfulRunCount);
        return unconvergedGroups
            .Select(g => g with { Variants = g.Variants.Where(s => s.SuccessfulRunCount == minRunCount).ToList() })
            .Where(g => g.Variants.Count > 0)
            .ToList();
    }

    protected override async Task OnPassCompleteAsync()
    {
        Console.WriteLine();
        Console.WriteLine("-------------------------------------------------------------------");
        Console.WriteLine("Benchmark runs complete. Generating analysis report automatically...");
        Console.WriteLine("-------------------------------------------------------------------");
        await AnalysisRunner.RunAsync(WorkingDirectory, Config, Selection, Ct);
    }
}
