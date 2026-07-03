namespace SmartCopy.Benchmarks;

internal sealed class BenchmarkCliOptions
{
    public BenchmarkRunMode Mode { get; init; } = BenchmarkRunMode.Benchmark;
    public const string DefaultConfigFileName = "benchmark-scenarios.json";

    public string? ScenarioName { get; init; }
    public string? VariantName { get; init; }
    public string? Notes { get; init; }
    public string ConfigPath { get; init; } = DefaultConfigFileName;
    public string? ComparePath { get; init; }
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
        string? comparePath = null;
        int? runs = null;

        var freshStart = false;
        var help = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--scenario", StringComparison.OrdinalIgnoreCase))
            {
                scenarioName = ReadRequiredValue(args, ref i, arg);
            }
            else if (string.Equals(arg, "--variant", StringComparison.OrdinalIgnoreCase))
            {
                variantName = ReadRequiredValue(args, ref i, arg);
            }
            else if (string.Equals(arg, "--notes", StringComparison.OrdinalIgnoreCase))
            {
                notes = ReadRequiredValue(args, ref i, arg);
            }
            else if (string.Equals(arg, "--mode", StringComparison.OrdinalIgnoreCase))
            {
                mode = ParseMode(ReadRequiredValue(args, ref i, arg));
            }
            else if (string.Equals(arg, "--config", StringComparison.OrdinalIgnoreCase))
            {
                configPath = ReadRequiredValue(args, ref i, arg);
            }
            else if (string.Equals(arg, "--runs", StringComparison.OrdinalIgnoreCase))
            {
                var raw = ReadRequiredValue(args, ref i, arg);
                if (!int.TryParse(raw, out var parsedRuns) || parsedRuns < 1)
                    throw new InvalidOperationException($"--runs must be a positive integer, got '{raw}'.");
                runs = parsedRuns;
            }
            else if (string.Equals(arg, "--fresh", StringComparison.OrdinalIgnoreCase))
            {
                freshStart = true;
            }
            else if (string.Equals(arg, "--compare-with", StringComparison.OrdinalIgnoreCase))
            {
                comparePath = ReadRequiredValue(args, ref i, arg);
            }
            else if (string.Equals(arg, "--remove", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(arg, "--remove-records", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(arg, "--prune", StringComparison.OrdinalIgnoreCase))
            {
                mode = BenchmarkRunMode.RemoveRecords;
            }
            else if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(arg, "-?", StringComparison.OrdinalIgnoreCase))
            {
                help = true;
            }
            else
            {
                throw new InvalidOperationException($"Unknown benchmark option '{arg}'. Use --help to see supported options.");
            }
        }

        return new BenchmarkCliOptions
        {
            Mode = mode,
            ScenarioName = scenarioName,
            VariantName = variantName,
            Notes = notes,
            ConfigPath = configPath,
            ComparePath = comparePath,
            FreshStart = freshStart,
            Help = help,
            Runs = runs,
        };
    }

    private static string ReadRequiredValue(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
            throw new InvalidOperationException($"{optionName} requires a value.");

        return args[++index];
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

        if (string.Equals(value, "compare", StringComparison.OrdinalIgnoreCase))
        {
            return BenchmarkRunMode.Compare;
        }

        if (string.Equals(value, "remove-records", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "remove", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "prune", StringComparison.OrdinalIgnoreCase))
        {
            return BenchmarkRunMode.RemoveRecords;
        }

        throw new InvalidOperationException($"Unknown benchmark mode '{value}'. Expected 'benchmark', 'dataset-prep', 'analysis', 'size-scaling', 'validation', 'compare', or 'remove-records'.");
    }
}

