namespace SmartCopy.Benchmarks;

/// <summary>
/// Runs benchmarks in convergence mode: repeatedly runs variants until each has a
/// pair of runs whose spread is within the configured convergence threshold, or
/// the maximum run count is reached.
/// </summary>
internal static class ConvergenceModeRunner
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

        Directory.CreateDirectory(artifactDirectory);

        // Step 1: Optional fresh start
        if (selection.FreshStart)
        {
            Console.WriteLine();
            Console.WriteLine("-------------------------------------------------------------------");
            Console.WriteLine("--fresh: Archiving existing results and resetting pool state...");
            Console.WriteLine("-------------------------------------------------------------------");

            await BenchmarkModeRunner.ArchiveResultsAsync(artifactDirectory, selection.ConfigPath, ct);
        }

        var historicalRuns = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkRunRecord>(resultsPath, ct);
        var enabledScenarios = config.Scenarios.Where(s => s.Enabled).ToList();
        var enabledVariants = config.Variants.Where(v => v.Enabled).ToList();

        // Step 2: Initial pass (only if needed)
        bool needsInitialPass = false;
        foreach (var scenario in enabledScenarios)
        {
            foreach (var variant in enabledVariants)
            {
                var successfulRuns = CountSuccessfulRuns(historicalRuns, scenario.Name, variant.Name);
                if (successfulRuns < variant.OriginalDesiredRunCount)
                {
                    needsInitialPass = true;
                    break;
                }
            }
            if (needsInitialPass) break;
        }

        if (needsInitialPass)
        {
            Console.WriteLine();
            Console.WriteLine("===================================================================");
            Console.WriteLine("CONVERGENCE MODE — Initial Pass");
            Console.WriteLine($"Convergence threshold: {config.ConvergenceSpreadPercent:F1}% spread");
            Console.WriteLine($"Max runs per variant:  {config.MaxRunsPerVariant}");
            Console.WriteLine("===================================================================");

            await BenchmarkModeRunner.RunAsync(workingDirectory, config, selection, ct, autoArchiveOnComplete: false);
            
            // Reload runs after initial pass
            historicalRuns = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkRunRecord>(resultsPath, ct);
        }

        // Step 3: Convergence loop
        Console.WriteLine();
        Console.WriteLine("===================================================================");
        Console.WriteLine("CONVERGENCE MODE — Convergence Loop");
        Console.WriteLine($"Convergence threshold: {config.ConvergenceSpreadPercent:F1}% spread");
        Console.WriteLine($"Max runs per variant:  {config.MaxRunsPerVariant}");
        Console.WriteLine("===================================================================");



        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var unconvergedVariants = new List<(BenchmarkScenario Scenario, BenchmarkVariant Variant)>();

            // Check convergence status for each scenario/variant pair
            foreach (var scenario in enabledScenarios)
            {
                foreach (var variant in enabledVariants)
                {
                    var status = CheckConvergence(historicalRuns, scenario.Name, variant.Name, config);

                    if (status == ConvergenceStatus.Converged)
                    {
                        continue;
                    }

                    if (status == ConvergenceStatus.GaveUp)
                    {
                        continue;
                    }

                    unconvergedVariants.Add((scenario, variant));
                }
            }

            if (unconvergedVariants.Count == 0)
            {
                break;
            }

            Console.WriteLine();
            Console.WriteLine($"{unconvergedVariants.Count} variant(s) still unconverged. Scheduling additional runs...");

            foreach (var (scenario, variant) in unconvergedVariants)
            {
                var spread = GetBestPairSpreadPercent(historicalRuns, scenario.Name, variant.Name);
                var spreadText = double.IsNaN(spread) ? "N/A" : $"{spread:F2}%";
                Console.WriteLine($"  • {scenario.Name}/{variant.Name} (current spread: {spreadText})");
            }

            // Temporarily bump DesiredRunCount for unconverged variants by 1,
            // then run another pass via BenchmarkModeRunner
            var originalCounts = new Dictionary<string, int>();
            foreach (var variant in enabledVariants)
            {
                originalCounts[variant.Name] = variant.DesiredRunCount;
            }

            try
            {
                foreach (var (scenario, variant) in unconvergedVariants)
                {
                    var currentSuccessful = CountSuccessfulRuns(historicalRuns, scenario.Name, variant.Name);
                    variant.DesiredRunCount = currentSuccessful + 1;
                }

                // Set any converged/gave-up variants to their current count so they don't re-run
                foreach (var scenario in enabledScenarios)
                {
                    foreach (var variant in enabledVariants)
                    {
                        if (!unconvergedVariants.Any(u => u.Scenario.Name == scenario.Name && u.Variant.Name == variant.Name))
                        {
                            var currentSuccessful = CountSuccessfulRuns(historicalRuns, scenario.Name, variant.Name);
                            variant.DesiredRunCount = currentSuccessful;
                        }
                    }
                }

                await BenchmarkModeRunner.RunAsync(workingDirectory, config, selection, ct, autoArchiveOnComplete: false);
            }
            finally
            {
                // Restore original counts
                foreach (var variant in enabledVariants)
                {
                    if (originalCounts.TryGetValue(variant.Name, out var original))
                    {
                        variant.DesiredRunCount = original;
                    }
                }
            }

            // Reload runs after the pass
            historicalRuns = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkRunRecord>(resultsPath, ct);
        }

        // Step 4: Print final convergence summary
        Console.WriteLine();
        Console.WriteLine("===================================================================");
        Console.WriteLine("CONVERGENCE MODE — Final Results");
        Console.WriteLine("===================================================================");

        foreach (var scenario in enabledScenarios)
        {
            foreach (var variant in enabledVariants)
            {
                var status = CheckConvergence(historicalRuns, scenario.Name, variant.Name, config);
                var successfulRuns = historicalRuns.Count(r =>
                    BenchmarkHelpers.IsSuccessfulRunForScenarioVariant(r, scenario.Name, variant.Name));
                var bestSpread = GetBestPairSpreadPercent(historicalRuns, scenario.Name, variant.Name);

                var statusText = status switch
                {
                    ConvergenceStatus.Converged => "✓ CONVERGED",
                    ConvergenceStatus.GaveUp => "✗ GAVE UP",
                    _ => "? UNKNOWN",
                };

                Console.WriteLine(
                    $"  {statusText}  {scenario.Name}/{variant.Name}  " +
                    $"({successfulRuns} runs, best pair spread: {bestSpread:F2}%)");
            }
        }

        Console.WriteLine();
    }

    private static int CountSuccessfulRuns(IReadOnlyList<BenchmarkRunRecord> runs, string scenarioName, string variantName)
    {
        return runs.Count(r =>
            string.Equals(r.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.VariantName, variantName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(r.RunStatus, BenchmarkRunStatus.Completed, StringComparison.OrdinalIgnoreCase) &&
            r.FailedFiles == 0 &&
            r.ExceptionType is null);
    }

    private enum ConvergenceStatus
    {
        NotConverged,
        Converged,
        GaveUp,
    }

    private static ConvergenceStatus CheckConvergence(
        IReadOnlyList<BenchmarkRunRecord> runs,
        string scenarioName,
        string variantName,
        BenchmarkConfig config)
    {
        var successfulRuns = runs
            .Where(r => BenchmarkHelpers.IsSuccessfulRunForScenarioVariant(r, scenarioName, variantName))
            .ToList();

        if (successfulRuns.Count < 2)
        {
            return successfulRuns.Count >= config.MaxRunsPerVariant
                ? ConvergenceStatus.GaveUp
                : ConvergenceStatus.NotConverged;
        }

        // Find best pair: the two runs with the smallest |duration_a - duration_b|
        var durations = successfulRuns
            .Select(r => r.ExecuteDuration.TotalSeconds)
            .OrderBy(d => d)
            .ToList();

        var bestSpread = double.MaxValue;
        for (var i = 1; i < durations.Count; i++)
        {
            var diff = durations[i] - durations[i - 1];
            if (diff < bestSpread)
            {
                bestSpread = diff;
            }
        }

        // Compute pair_spread_pct = |diff| / mean * 100
        // Find the actual pair values for the best spread
        double bestA = 0, bestB = 0;
        for (var i = 1; i < durations.Count; i++)
        {
            var diff = durations[i] - durations[i - 1];
            if (Math.Abs(diff - bestSpread) < 0.0001)
            {
                bestA = durations[i - 1];
                bestB = durations[i];
                break;
            }
        }

        var pairMean = (bestA + bestB) / 2.0;
        var pairSpreadPct = pairMean > 0 ? (bestSpread / pairMean) * 100.0 : 0.0;

        if (pairSpreadPct <= config.ConvergenceSpreadPercent)
        {
            return ConvergenceStatus.Converged;
        }

        return successfulRuns.Count >= config.MaxRunsPerVariant
            ? ConvergenceStatus.GaveUp
            : ConvergenceStatus.NotConverged;
    }

    private static double GetBestPairSpreadPercent(
        IReadOnlyList<BenchmarkRunRecord> runs,
        string scenarioName,
        string variantName)
    {
        var durations = runs
            .Where(r => BenchmarkHelpers.IsSuccessfulRunForScenarioVariant(r, scenarioName, variantName))
            .Select(r => r.ExecuteDuration.TotalSeconds)
            .OrderBy(d => d)
            .ToList();

        if (durations.Count < 2)
        {
            return double.NaN;
        }

        var bestDiff = double.MaxValue;
        double bestA = 0, bestB = 0;
        for (var i = 1; i < durations.Count; i++)
        {
            var diff = durations[i] - durations[i - 1];
            if (diff < bestDiff)
            {
                bestDiff = diff;
                bestA = durations[i - 1];
                bestB = durations[i];
            }
        }

        var pairMean = (bestA + bestB) / 2.0;
        return pairMean > 0 ? (bestDiff / pairMean) * 100.0 : 0.0;
    }
}
