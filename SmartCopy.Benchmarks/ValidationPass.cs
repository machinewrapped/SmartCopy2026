namespace SmartCopy.Benchmarks;

/// <summary>
/// Depth-first validation scheduler for <c>--mode validation</c>. Runs scenarios in execution
/// order: advances the first unconverged scenario's lowest-run-count variants (so matched pairs
/// interleave + shuffle in one round), then gate-checks each fully-converged scenario before
/// advancing. When <see cref="BenchmarkConfig.FailFast"/> is true, halts the whole pass on
/// REGRESSION/INVALID so a regression on a cheap pair stops before the expensive pairs run. Re-derived
/// from <see cref="BenchmarkPassBase.HistoricalRuns"/> each call, so a cold-boot resume re-checks
/// earlier scenarios and halts immediately if one had already regressed.
/// </summary>
internal sealed class ValidationPass : BenchmarkPassBase
{
    private (string Scenario, string Variant, string Verdict)? _validationHalt;

    internal ValidationPass(
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
        // --variant and --runs bypass the scheduler (one-shot invocation).
        // --scenario only filters the scenario list — the validation scheduler still runs
        // (depth-first, gate-check after convergence, optional fail-fast), so
        // `--mode validate --scenario X` means "validate X to convergence".
        var isExplicit = !string.IsNullOrWhiteSpace(Selection.VariantName)
                      || Selection.Runs.HasValue;
        var explicitRuns = Selection.Runs ?? 1;

        if (isExplicit && sessionRoundsCompleted >= explicitRuns)
            return [];

        var groups = BuildScenarioGroups();

        if (isExplicit)
            return groups;

        return DetermineValidationVariants(groups);
    }

    /// <summary>
    /// Walks scenario groups in execution order: returns the first one with unconverged variants
    /// (running its lowest-run-count variants so a round still interleaves+shuffles the matched
    /// pair); for each fully-converged scenario reached before it, evaluates the gate and stops
    /// the whole pass on REGRESSION/INVALID.
    /// </summary>
    private List<BenchmarkScenarioGroup> DetermineValidationVariants(List<BenchmarkScenarioGroup> groups)
    {
        foreach (var group in groups)
        {
            var unconverged = group.Variants
                .Where(s => BenchmarkConvergence.Check(HistoricalRuns, s.Scenario.Name, s.Variant, Config)
                            == BenchmarkConvergence.Status.NotConverged)
                .ToList();

            if (unconverged.Count > 0)
            {
                var minRunCount = unconverged.Min(s => s.SuccessfulRunCount);
                var round = unconverged.Where(s => s.SuccessfulRunCount == minRunCount).ToList();
                return [group with { Variants = round }];
            }

            // Scenario fully converged/gave up — gate-check it before moving to the next.
            var scenarioRuns = HistoricalRuns
                .Where(r => string.Equals(r.ScenarioName, group.Scenario.Name, StringComparison.OrdinalIgnoreCase))
                .Where(BenchmarkHelpers.IsTerminalRun)
                .ToList();

            if (Config.FailFast &&
                ValidationGate.ScenarioFailedGate(Config, scenarioRuns, out var failVariant, out var failVerdict))
            {
                _validationHalt = (group.Scenario.Name, failVariant ?? "?", failVerdict);
                return [];
            }
        }

        return [];
    }

    protected override void OnPassHalted()
    {
        if (_validationHalt is { } halt)
        {
            Console.WriteLine();
            Console.WriteLine("-------------------------------------------------------------------");
            Console.WriteLine($"FAIL-FAST HALT: {halt.Verdict} at scenario '{halt.Scenario}' (variant '{halt.Variant}').");
            Console.WriteLine("Remaining scenarios skipped. See the validation conclusion below.");
            Console.WriteLine("-------------------------------------------------------------------");
        }
        else
        {
            Console.WriteLine("All eligible validation scenarios and variants have completed/converged.");
        }
    }

    protected override async Task OnPassCompleteAsync()
    {
        Console.WriteLine();
        Console.WriteLine("-------------------------------------------------------------------");
        Console.WriteLine("Validation runs complete. Generating analysis report and validation conclusion...");
        Console.WriteLine("-------------------------------------------------------------------");

        await AnalysisRunner.RunAsync(WorkingDirectory, Config, Selection, Ct);
        await ValidationConclusionReporter.RunAsync(WorkingDirectory, Config, Selection, Ct);
    }
}
