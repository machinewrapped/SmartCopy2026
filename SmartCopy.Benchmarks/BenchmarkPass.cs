using System.Text;

namespace SmartCopy.Benchmarks;

internal sealed class BenchmarkPass
{
    private readonly BenchmarkConfig _config;
    private readonly BenchmarkCliOptions _selection;
    private readonly SessionPaths _paths;
    private readonly string _workingDirectory;
    private readonly CancellationToken _ct;
    private List<BenchmarkRunRecord> _historicalRuns = [];

    internal BenchmarkPass(
        string workingDirectory,
        BenchmarkConfig config,
        BenchmarkCliOptions selection,
        SessionPaths paths,
        CancellationToken ct)
    {
        _workingDirectory = workingDirectory;
        _config = config;
        _selection = selection;
        _paths = paths;
        _ct = ct;
    }

    internal async Task ExecuteAsync()
    {
        _historicalRuns = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkRunRecord>(_paths.ResultsPath, _ct);
        await PrepareSessionAsync();
        await UpdateTaskListAsync();
        
        Console.WriteLine();
        Console.WriteLine($"Artifacts directory: {_paths.ArtifactDirectory}");

        var sessionRoundsCompleted = 0;
        var isFirstRound = true;
        BenchmarkScenario? lastScenario = null;

        while (true)
        {
            var roundVariants = DetermineVariantsToRun(sessionRoundsCompleted);
            if (roundVariants.Count == 0)
            {
                Console.WriteLine("All eligible benchmark scenarios and variants have completed/converged.");
                break;
            }

            PrintBenchmarkQueue(roundVariants);
            var round = new BenchmarkRound(roundVariants, _config, _selection, _paths, _historicalRuns, _ct);
            lastScenario = await round.RunAsync(isFirstRound, lastScenario, UpdateTaskListAsync);
            isFirstRound = false;
            sessionRoundsCompleted++;
        }

        Console.WriteLine();
        Console.WriteLine("-------------------------------------------------------------------");
        Console.WriteLine("Benchmark runs complete. Generating analysis report automatically...");
        Console.WriteLine("-------------------------------------------------------------------");
        await AnalysisRunner.RunAsync(_workingDirectory, _config, _selection, _ct);
    }

    private async Task PrepareSessionAsync()
    {
        var isFiltered = !string.IsNullOrWhiteSpace(_selection.ScenarioName) || !string.IsNullOrWhiteSpace(_selection.VariantName);

        if (_selection.FreshStart)
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
                await BenchmarkModeRunner.ArchiveResultsAsync(_paths.ArtifactDirectory, _selection.ConfigPath, _ct);
                _historicalRuns = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkRunRecord>(_paths.ResultsPath, _ct);
            }
            return;
        }

        if (_historicalRuns.Count > 0 && !isFiltered && !HasAnyPendingRuns())
        {
            Console.WriteLine();
            Console.WriteLine("-------------------------------------------------------------------");
            Console.WriteLine("All previous benchmark runs in the active scenarios are completed/converged.");
            Console.WriteLine("Archiving completed runs to a dated subfolder to start fresh...");
            Console.WriteLine("-------------------------------------------------------------------");
            await BenchmarkModeRunner.ArchiveResultsAsync(_paths.ArtifactDirectory, _selection.ConfigPath, _ct);
            _historicalRuns = await BenchmarkHelpers.ReadExistingRunsAsync<BenchmarkRunRecord>(_paths.ResultsPath, _ct);
        }
    }

    private bool HasAnyPendingRuns()
    {
        foreach (var scenario in _config.Scenarios.Where(s => s.Enabled))
            foreach (var variant in _config.Variants.Where(v => v.Enabled))
                if (BenchmarkConvergence.Check(_historicalRuns, scenario.Name, variant, _config) == BenchmarkConvergence.Status.NotConverged)
                    return true;
        return false;
    }

    private List<BenchmarkScenarioGroup> DetermineVariantsToRun(int sessionRoundsCompleted)
    {
        var isExplicit = !string.IsNullOrWhiteSpace(_selection.ScenarioName)
                      || !string.IsNullOrWhiteSpace(_selection.VariantName)
                      || _selection.Runs.HasValue;
        var explicitRuns = _selection.Runs ?? 1;

        if (isExplicit && sessionRoundsCompleted >= explicitRuns)
            return [];

        var scenarioOrder = BenchmarkHelpers.BuildScenarioOrder(_config, _config.Scenarios.Where(s => s.Enabled).Select(s => s.Name));
        if (!string.IsNullOrWhiteSpace(_selection.ScenarioName))
            scenarioOrder = scenarioOrder.Where(n => string.Equals(n, _selection.ScenarioName, StringComparison.OrdinalIgnoreCase)).ToList();

        var groups = new List<BenchmarkScenarioGroup>();
        foreach (var scenarioName in scenarioOrder)
        {
            var scenario = _config.Scenarios.First(s => string.Equals(s.Name, scenarioName, StringComparison.OrdinalIgnoreCase));
            var variants = new List<BenchmarkSelection>();
            foreach (var variant in _config.Variants.Where(v => v.Enabled))
            {
                if (scenario.Variants is { Count: > 0 } && !scenario.Variants.Contains(variant.Name, StringComparer.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrWhiteSpace(_selection.VariantName) &&
                    !string.Equals(variant.Name, _selection.VariantName, StringComparison.OrdinalIgnoreCase))
                    continue;
                var successfulRuns = _historicalRuns.Count(r => BenchmarkHelpers.IsSuccessfulRunForScenarioVariant(r, scenario.Name, variant.Name));
                var totalRuns = _historicalRuns.Count(r => BenchmarkHelpers.IsTerminalRunForScenarioVariant(r, scenario.Name, variant.Name));
                var lastRunUtc = _historicalRuns
                    .Where(r => BenchmarkHelpers.IsRunForScenarioVariant(r, scenario.Name, variant.Name))
                    .Select(r => r.RunStartedUtc).DefaultIfEmpty(DateTime.MinValue).Max();
                variants.Add(new BenchmarkSelection(scenario, variant, successfulRuns, totalRuns, lastRunUtc, successfulRuns + 1));
            }
            if (variants.Count > 0)
                groups.Add(new BenchmarkScenarioGroup(scenario, variants));
        }

        if (isExplicit)
            return groups;

        var unconvergedGroups = groups
            .Select(g => g with { Variants = g.Variants
                .Where(s => BenchmarkConvergence.Check(_historicalRuns, s.Scenario.Name, s.Variant, _config)
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

    private void PrintBenchmarkQueue(IReadOnlyList<BenchmarkScenarioGroup> groups)
    {
        Console.WriteLine();
        Console.WriteLine("Benchmark Queue:");
        foreach (var candidate in groups.SelectMany(g => g.Variants))
        {
            var spread = BenchmarkConvergence.GetCurrentSpreadPercent(_historicalRuns, candidate.Scenario.Name, candidate.Variant, _config);
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
        builder.AppendLine($"Source: `{Path.GetFullPath(_config.SourcePath)}`");

        var scenarioOrder = BenchmarkHelpers.BuildScenarioOrder(_config, _config.Scenarios.Where(s => s.Enabled).Select(s => s.Name));
        if (scenarioOrder.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"Scenario execution order: `{string.Join(" -> ", scenarioOrder)}`");
        }

        builder.AppendLine();
        builder.AppendLine("| Scenario | Variant | Destination | Target runs | Successful runs | Last run (UTC) | Last status |");
        builder.AppendLine("|---|---|---|---:|---:|---|---|");

        foreach (var variant in _config.Variants.Where(v => v.Enabled))
        {
            foreach (var scenario in _config.Scenarios.Where(s => s.Enabled))
            {
                var scenarioRuns = _historicalRuns
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

        await File.WriteAllTextAsync(_paths.TaskListPath, builder.ToString(), _ct);
    }
}
