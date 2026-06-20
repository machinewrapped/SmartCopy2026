using System.Text;

namespace SmartCopy.Benchmarks;

/// <summary>
/// Shared infrastructure for <see cref="BenchmarkPass"/> and <see cref="ValidationPass"/>:
/// session preparation, task-list updates, the round loop skeleton, and scenario-group
/// building. Subclasses implement <see cref="DetermineVariantsToRun"/> (scheduling) and
/// optionally override <see cref="OnPassHalted"/> / <see cref="OnPassCompleteAsync"/>.
/// </summary>
internal abstract class BenchmarkPassBase
{
    protected readonly string WorkingDirectory;
    protected readonly BenchmarkConfig Config;
    protected readonly BenchmarkCliOptions Selection;
    protected readonly SessionPaths Paths;
    protected readonly CancellationToken Ct;
    protected List<BenchmarkRunRecord> HistoricalRuns = [];

    protected BenchmarkPassBase(
        string workingDirectory,
        BenchmarkConfig config,
        BenchmarkCliOptions selection,
        SessionPaths paths,
        CancellationToken ct)
    {
        WorkingDirectory = workingDirectory;
        Config = config;
        Selection = selection;
        Paths = paths;
        Ct = ct;
    }

    internal async Task ExecuteAsync()
    {
        HistoricalRuns = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkRunRecord>(Paths.ResultsPath, Ct);
        await PrepareSessionAsync();
        await UpdateTaskListAsync();

        Console.WriteLine();
        Console.WriteLine($"Artifacts directory: {Paths.ArtifactDirectory}");

        var sessionRoundsCompleted = 0;
        var isFirstRound = true;
        BenchmarkScenario? lastScenario = null;

        while (true)
        {
            var roundVariants = DetermineVariantsToRun(sessionRoundsCompleted);
            if (roundVariants.Count == 0)
            {
                OnPassHalted();
                break;
            }

            PrintBenchmarkQueue(roundVariants);
            var round = new BenchmarkRound(roundVariants, Config, Selection, Paths, HistoricalRuns, Ct);
            lastScenario = await round.RunAsync(isFirstRound, lastScenario, UpdateTaskListAsync);
            isFirstRound = false;
            sessionRoundsCompleted++;
        }

        await OnPassCompleteAsync();
    }

    /// <summary>
    /// Scheduling decision: which scenario/variant combinations to run in the next round.
    /// Return an empty list to halt the pass.
    /// </summary>
    protected abstract List<BenchmarkScenarioGroup> DetermineVariantsToRun(int sessionRoundsCompleted);

    /// <summary>Called when the pass halts (no more variants to run). Override for mode-specific messaging.</summary>
    protected virtual void OnPassHalted()
    {
        Console.WriteLine("All eligible benchmark scenarios and variants have completed/converged.");
    }

    /// <summary>Called after the round loop exits, before the pass returns. Override for analysis/reporting.</summary>
    protected virtual Task OnPassCompleteAsync() => Task.CompletedTask;

    /// <summary>
    /// Builds the full set of eligible scenario groups (filtered by --scenario/--variant and
    /// per-scenario variant lists), with run-count statistics from <see cref="HistoricalRuns"/>.
    /// Shared by both modes; each mode's scheduler then filters further.
    /// </summary>
    protected List<BenchmarkScenarioGroup> BuildScenarioGroups()
    {
        var scenarioOrder = BenchmarkHelpers.BuildScenarioOrder(Config, Config.Scenarios.Where(s => s.Enabled).Select(s => s.Name));
        if (!string.IsNullOrWhiteSpace(Selection.ScenarioName))
            scenarioOrder = scenarioOrder.Where(n => string.Equals(n, Selection.ScenarioName, StringComparison.OrdinalIgnoreCase)).ToList();

        var groups = new List<BenchmarkScenarioGroup>();
        foreach (var scenarioName in scenarioOrder)
        {
            var scenario = Config.Scenarios.First(s => string.Equals(s.Name, scenarioName, StringComparison.OrdinalIgnoreCase));
            var variants = new List<BenchmarkSelection>();
            foreach (var variant in Config.Variants.Where(v => v.Enabled))
            {
                if (scenario.Variants is { Count: > 0 } && !scenario.Variants.Contains(variant.Name, StringComparer.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(Selection.VariantName) &&
                    !string.Equals(variant.Name, Selection.VariantName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var successfulRuns = HistoricalRuns.Count(r => BenchmarkHelpers.IsSuccessfulRunForScenarioVariant(r, scenario.Name, variant.Name));
                var totalRuns = HistoricalRuns.Count(r => BenchmarkHelpers.IsTerminalRunForScenarioVariant(r, scenario.Name, variant.Name));
                var lastRunUtc = HistoricalRuns
                    .Where(r => BenchmarkHelpers.IsRunForScenarioVariant(r, scenario.Name, variant.Name))
                    .Select(r => r.RunStartedUtc).DefaultIfEmpty(DateTime.MinValue).Max();
                variants.Add(new BenchmarkSelection(scenario, variant, successfulRuns, totalRuns, lastRunUtc, successfulRuns + 1));
            }
            if (variants.Count > 0)
                groups.Add(new BenchmarkScenarioGroup(scenario, variants));
        }

        return groups;
    }

    private async Task PrepareSessionAsync()
    {
        var isFiltered = !string.IsNullOrWhiteSpace(Selection.ScenarioName) || !string.IsNullOrWhiteSpace(Selection.VariantName);

        if (Selection.FreshStart)
        {
            if (isFiltered)
            {
                Console.WriteLine();
                Console.WriteLine("-------------------------------------------------------------------");
                Console.WriteLine("Fresh start requested via --fresh flag with a scenario/variant filter.");
                Console.WriteLine("Bypassing archive to preserve existing runs. Will force an additional run.");
                Console.WriteLine("-------------------------------------------------------------------");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("-------------------------------------------------------------------");
                Console.WriteLine("Fresh start requested via --fresh flag.");
                Console.WriteLine("Archiving completed runs to a dated subfolder to start fresh...");
                Console.WriteLine("-------------------------------------------------------------------");
                await BenchmarkModeRunner.ArchiveResultsAsync(Paths.ArtifactDirectory, Selection.ConfigPath, Ct);
                HistoricalRuns = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkRunRecord>(Paths.ResultsPath, Ct);
            }
            return;
        }

        if (HistoricalRuns.Count > 0 && !isFiltered && !HasAnyPendingRuns())
        {
            Console.WriteLine();
            Console.WriteLine("-------------------------------------------------------------------");
            Console.WriteLine("All previous benchmark runs in the active scenarios are completed/converged.");
            Console.WriteLine("Archiving completed runs to a dated subfolder to start fresh...");
            Console.WriteLine("-------------------------------------------------------------------");
            await BenchmarkModeRunner.ArchiveResultsAsync(Paths.ArtifactDirectory, Selection.ConfigPath, Ct);
            HistoricalRuns = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkRunRecord>(Paths.ResultsPath, Ct);
        }
    }

    private bool HasAnyPendingRuns()
    {
        foreach (var scenario in Config.Scenarios.Where(s => s.Enabled))
            foreach (var variant in Config.Variants.Where(v => v.Enabled))
                if (BenchmarkConvergence.Check(HistoricalRuns, scenario.Name, variant, Config) == BenchmarkConvergence.Status.NotConverged)
                    return true;
        return false;
    }

    private void PrintBenchmarkQueue(IReadOnlyList<BenchmarkScenarioGroup> groups)
    {
        Console.WriteLine();
        Console.WriteLine("Benchmark Queue:");
        foreach (var candidate in groups.SelectMany(g => g.Variants))
        {
            var spread = BenchmarkConvergence.GetCurrentSpreadPercent(HistoricalRuns, candidate.Scenario.Name, candidate.Variant, Config);
            var spreadText = double.IsNaN(spread) ? "N/A" : $"{spread:F2}%";
            Console.WriteLine($"  • [{candidate.Scenario.Name}] {candidate.Variant.Name} (Run {candidate.NextRunIndex}/{candidate.Variant.DesiredRunCount}, spread: {spreadText})");
        }
        Console.WriteLine();
    }

    internal async Task UpdateTaskListAsync()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Benchmark Task List");
        builder.AppendLine();
        builder.AppendLine($"Source: `{Path.GetFullPath(Config.SourcePath)}`");

        var scenarioOrder = BenchmarkHelpers.BuildScenarioOrder(Config, Config.Scenarios.Where(s => s.Enabled).Select(s => s.Name));
        if (scenarioOrder.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"Scenario execution order: `{string.Join(" -> ", scenarioOrder)}`");
        }

        builder.AppendLine();
        builder.AppendLine("| Scenario | Variant | Destination | Target runs | Successful runs | Last run (UTC) | Last status |");
        builder.AppendLine("|---|---|---|---:|---:|---|---|");

        foreach (var variant in Config.Variants.Where(v => v.Enabled))
        {
            foreach (var scenario in Config.Scenarios.Where(s => s.Enabled))
            {
                var scenarioRuns = HistoricalRuns
                    .Where(r => BenchmarkHelpers.IsRunForScenarioVariant(r, scenario.Name, variant.Name))
                    .OrderByDescending(r => r.RunStartedUtc)
                    .ThenByDescending(BenchmarkHelpers.IsTerminalRun)
                    .ToList();
                var lastRun = scenarioRuns.FirstOrDefault();
                var successfulRuns = scenarioRuns.Count(r =>
                    string.Equals(r.RunStatus, BenchmarkRunStatus.Completed, StringComparison.OrdinalIgnoreCase) &&
                    r.FailedFiles == 0 &&
                    r.ExceptionType is null);
                var lastStatus = lastRun is null
                    ? "pending"
                    : string.Equals(lastRun.RunStatus, BenchmarkRunStatus.InProgress, StringComparison.OrdinalIgnoreCase)
                        ? "in progress"
                        : lastRun.ExceptionType is not null
                        ? $"error ({lastRun.ExceptionType})"
                        : lastRun.FailedFiles > 0
                            ? $"failed ({lastRun.FailedFiles})"
                            : "ok";

                builder.Append("| ")
                    .Append(BenchmarkHelpers.EscapeTable(scenario.Name)).Append(" | ")
                    .Append(BenchmarkHelpers.EscapeTable(variant.Name)).Append(" | ")
                    .Append(BenchmarkHelpers.EscapeTable(Path.GetFullPath(scenario.DestinationPath))).Append(" | ")
                    .Append(variant.DesiredRunCount).Append(" | ")
                    .Append(successfulRuns).Append(" | ")
                    .Append(lastRun?.RunStartedUtc.ToString("O") ?? "-").Append(" | ")
                    .Append(BenchmarkHelpers.EscapeTable(lastStatus)).AppendLine(" |");
            }
        }

        await File.WriteAllTextAsync(Paths.TaskListPath, builder.ToString(), Ct);
    }
}
