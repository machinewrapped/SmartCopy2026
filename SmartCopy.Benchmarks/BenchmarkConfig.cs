using SmartCopy.Core.FileSystem;

namespace SmartCopy.Benchmarks;

internal sealed class BenchmarkConfig
{
    public string SourcePath { get; set; } = string.Empty;
    public string? ArtifactPath { get; set; }
    public bool IncludeHidden { get; set; }
    public double ConvergenceSpreadPercent { get; set; } = 3.0;
    public double GatePercent { get; set; } = 3.0;
    public int MaxConvergenceRuns { get; set; } = 5;
    public bool Converge { get; set; } = true;
    public bool FailFast { get; set; } = true;
    public bool ClearDestinationBeforeRun { get; set; } = true;
    public bool ClearDestinationAfterRun { get; set; } = true;
    public List<BenchmarkScenario> Scenarios { get; set; } = [];
    public List<string> ScenarioExecutionOrder { get; set; } = [];
    public List<BenchmarkVariant> Variants { get; set; } = [];
    public DatasetPreparationConfig? DatasetPreparation { get; set; }
    public int CooldownSeconds { get; set; } = 60;

    /// <summary>
    /// Keyed by normalized base source path. Each value is the (shuffled) list of pool
    /// paths for that source root, shared across all scenarios that use that source.
    /// Populated on first benchmark run; persisted to the config file.
    /// </summary>
    public Dictionary<string, List<string>> SourcePools { get; set; } = new();

    /// <summary>
    /// Creates a machine-neutral starting point for a new benchmark configuration.
    /// Fill in the source path and at least one scenario before running a benchmark.
    /// </summary>
    public static BenchmarkConfig CreateScaffold() => new();

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            throw new InvalidOperationException("sourcePath is required.");
        }

        SourcePath = Path.GetFullPath(SourcePath);
        ArtifactPath = string.IsNullOrWhiteSpace(ArtifactPath)
            ? null
            : Path.GetFullPath(ArtifactPath);

        foreach (var scenario in Scenarios)
        {
            scenario.Normalize();
        }

        ScenarioExecutionOrder = ScenarioExecutionOrder
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (Variants.Count == 0)
        {
            Variants.Add(new BenchmarkVariant
            {
                Name = "ScenarioDefaults",
                Notes = "Synthesized for legacy configs without a variants section.",
                DesiredRunCount = 1,
            });
        }

        foreach (var variant in Variants)
        {
            variant.Normalize();
        }

        DatasetPreparation?.Normalize();
    }
}
