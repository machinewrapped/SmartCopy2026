namespace SmartCopy.Benchmarks;

internal sealed class BenchmarkRound
{
    private readonly IReadOnlyList<BenchmarkScenarioGroup> _scenarioGroups;
    private readonly BenchmarkConfig _config;
    private readonly BenchmarkCliOptions _cliOptions;
    private readonly SessionPaths _paths;
    private readonly List<BenchmarkRunRecord> _historicalRuns;
    private readonly CancellationToken _ct;

    internal BenchmarkRound(
        IReadOnlyList<BenchmarkScenarioGroup> scenarioGroups,
        BenchmarkConfig config,
        BenchmarkCliOptions cliOptions,
        SessionPaths paths,
        List<BenchmarkRunRecord> historicalRuns,
        CancellationToken ct)
    {
        _scenarioGroups = scenarioGroups;
        _config = config;
        _cliOptions = cliOptions;
        _paths = paths;
        _historicalRuns = historicalRuns;
        _ct = ct;
    }

    internal async Task<BenchmarkScenario?> RunAsync(bool isFirstRound, BenchmarkScenario? lastScenario, Func<Task> onTaskListUpdate)
    {
        var taskIndex = 0;
        foreach (var scenarioGroup in _scenarioGroups)
        {
            var shuffled = scenarioGroup.Variants.ToArray();
            Random.Shared.Shuffle(shuffled);

            foreach (var selection in shuffled)
            {
                var skipCooldown = taskIndex == 0 && isFirstRound;
                if (!skipCooldown && _config.CooldownSeconds > 0)
                    await ApplyCooldownAsync();

                var task = new BenchmarkTask(selection, _config, _cliOptions, _paths, _historicalRuns, onTaskListUpdate, _ct);
                await task.ExecuteAsync(lastScenario);
                lastScenario = scenarioGroup.Scenario;
                taskIndex++;
            }
        }

        return lastScenario;
    }

    private async Task ApplyCooldownAsync()
    {
        for (int i = _config.CooldownSeconds; i > 0; i--)
        {
            if (_ct.IsCancellationRequested) break;
            BenchmarkHelpers.UpdateProgress($"Applying cooldown to settle drive cache... resuming in {i}s");
            await Task.Delay(1000, _ct);
        }
        BenchmarkHelpers.UpdateProgress("");
    }
}
