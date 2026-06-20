namespace SmartCopy.Benchmarks;

internal sealed class BenchmarkCliOptions
{
    public BenchmarkRunMode Mode { get; init; } = BenchmarkRunMode.Benchmark;
    public const string DefaultConfigFileName = "benchmark-scenarios.json";

    public string? ScenarioName { get; init; }
    public string? VariantName { get; init; }
    public string? Notes { get; init; }
    public string ConfigPath { get; init; } = DefaultConfigFileName;
    public bool FreshStart { get; init; }
    public bool Help { get; init; }
    public int? Runs { get; init; }

    public static BenchmarkCliOptions Parse(string[] args)
    {
        var mode = BenchmarkRunMode.Benchmark;
        string? scenarioName = null;
        string? variantName = null;
        string? notes = null;
        var configPath = DefaultConfigFileName;
        int? runs = null;

        var freshStart = false;
        var help = false;

        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--scenario", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                scenarioName = args[++i];
            }
            else if (string.Equals(args[i], "--variant", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                variantName = args[++i];
            }
            else if (string.Equals(args[i], "--notes", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                notes = args[++i];
            }
            else if (string.Equals(args[i], "--mode", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                mode = ParseMode(args[++i]);
            }
            else if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                configPath = args[++i];
            }
            else if (string.Equals(args[i], "--runs", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                var raw = args[++i];
                if (!int.TryParse(raw, out var parsedRuns) || parsedRuns < 1)
                    throw new InvalidOperationException($"--runs must be a positive integer, got '{raw}'.");
                runs = parsedRuns;
            }
            else if (string.Equals(args[i], "--fresh", StringComparison.OrdinalIgnoreCase))
            {
                freshStart = true;
            }
            else if (string.Equals(args[i], "--help", StringComparison.OrdinalIgnoreCase) || 
                     string.Equals(args[i], "-h", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(args[i], "-?", StringComparison.OrdinalIgnoreCase))
            {
                help = true;
            }
        }

        return new BenchmarkCliOptions
        {
            Mode = mode,
            ScenarioName = scenarioName,
            VariantName = variantName,
            Notes = notes,
            ConfigPath = configPath,
            FreshStart = freshStart,
            Help = help,
            Runs = runs,
        };
    }

    private static BenchmarkRunMode ParseMode(string value)
    {
        if (string.Equals(value, "benchmark", StringComparison.OrdinalIgnoreCase))
        {
            return BenchmarkRunMode.Benchmark;
        }

        if (string.Equals(value, "dataset-prep", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "dataset-preparation", StringComparison.OrdinalIgnoreCase))
        {
            return BenchmarkRunMode.DatasetPreparation;
        }

        if (string.Equals(value, "analysis", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "analyze", StringComparison.OrdinalIgnoreCase))
        {
            return BenchmarkRunMode.Analysis;
        }

        if (string.Equals(value, "size-scaling", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "scaling", StringComparison.OrdinalIgnoreCase))
        {
            return BenchmarkRunMode.SizeScaling;
        }

        if (string.Equals(value, "validation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "validate", StringComparison.OrdinalIgnoreCase))
        {
            return BenchmarkRunMode.Validation;
        }

        throw new InvalidOperationException($"Unknown benchmark mode '{value}'. Expected 'benchmark', 'dataset-prep', 'analysis', 'size-scaling', or 'validation'.");
    }
}

