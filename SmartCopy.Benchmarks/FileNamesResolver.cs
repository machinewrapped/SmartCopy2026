namespace SmartCopy.Benchmarks;

internal sealed record BenchmarkFileNames(
    string Results,
    string FileResults,
    string Analysis,
    string SizeScaling,
    string TaskList);

internal static class FileNamesResolver
{
    public const string DefaultResults = "benchmark-results.ndjson";
    public const string DefaultFileResults = "benchmark-file-results.ndjson";
    public const string DefaultAnalysis = "benchmark-analysis.md";
    public const string DefaultSizeScaling = "benchmark-size-scaling.md";
    public const string DefaultTaskList = "benchmark-tasklist.md";

    public static BenchmarkFileNames GetFileNames(string configPath)
    {
        var configFileName = Path.GetFileNameWithoutExtension(configPath);
        var baseName = configFileName;

        string[] prefixes =
        [
            "benchmark-scenarios-",
            "benchmark-scenarios",
            "benchmark-",
            "benchmark",
            "validation-",
            "validation"
        ];

        foreach (var p in prefixes)
        {
            if (baseName.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName[p.Length..];
                break;
            }
        }

        baseName = baseName.TrimStart('-');

        if (string.IsNullOrEmpty(baseName))
        {
            return new(DefaultResults, DefaultFileResults, DefaultAnalysis, DefaultSizeScaling, DefaultTaskList);
        }

        return new(
            $"results-{baseName}.ndjson",
            $"file-results-{baseName}.ndjson",
            $"analysis-{baseName}.md",
            $"size-scaling-{baseName}.md",
            $"tasklist-{baseName}.md"
        );
    }
}
