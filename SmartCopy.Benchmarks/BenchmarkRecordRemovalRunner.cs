using System.Text.Json;

namespace SmartCopy.Benchmarks;

internal static class BenchmarkRecordRemovalRunner
{
    public static async Task RunAsync(
        string workingDirectory,
        BenchmarkConfig config,
        BenchmarkCliOptions selection,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(selection.ScenarioName))
            throw new InvalidOperationException("--mode remove-records requires --scenario <name>.");

        var fileNames = FileNamesResolver.GetFileNames(selection.ConfigPath);
        var artifactDirectory = BenchmarkHelpers.ResolveArtifactDirectory(workingDirectory, config.SourcePath, config.ArtifactPath);
        var resultsPath = Path.Combine(artifactDirectory, fileNames.Results);
        var fileResultsPath = Path.Combine(artifactDirectory, fileNames.FileResults);

        Console.WriteLine();
        Console.WriteLine("-------------------------------------------------------------------");
        Console.WriteLine("Removing benchmark records");
        Console.WriteLine($"Scenario: {selection.ScenarioName}");
        if (!string.IsNullOrWhiteSpace(selection.VariantName))
            Console.WriteLine($"Variant:  {selection.VariantName}");
        Console.WriteLine("-------------------------------------------------------------------");

        var runRecordsRemoved = await RemoveMatchingRecordsAsync<BenchmarkRunRecord>(
            resultsPath,
            r => IsSelectedRecord(selection, r.ScenarioName, r.VariantName),
            "run-level",
            ct);

        var fileRecordsRemoved = await RemoveMatchingRecordsAsync<BenchmarkFileCopyRecord>(
            fileResultsPath,
            r => IsSelectedRecord(selection, r.ScenarioName, r.VariantName),
            "file-level",
            ct);

        Console.WriteLine();
        Console.WriteLine($"Removed {runRecordsRemoved} run-level record(s).");
        Console.WriteLine($"Removed {fileRecordsRemoved} file-level record(s).");
        Console.WriteLine();
        Console.WriteLine("Run analysis again to regenerate reports from the remaining records.");
    }

    private static bool IsSelectedRecord(BenchmarkCliOptions selection, string scenarioName, string variantName)
    {
        if (!string.Equals(scenarioName, selection.ScenarioName, StringComparison.OrdinalIgnoreCase))
            return false;

        return string.IsNullOrWhiteSpace(selection.VariantName) ||
               string.Equals(variantName, selection.VariantName, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> RemoveMatchingRecordsAsync<T>(
        string path,
        Func<T, bool> shouldRemove,
        string label,
        CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"No {label} results file found: {path}");
            return 0;
        }

        var lines = await File.ReadAllLinesAsync(path, ct);
        var retained = new List<string>(lines.Length);
        var removed = 0;

        foreach (var line in lines)
        {
            ct.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(line))
            {
                retained.Add(line);
                continue;
            }

            T? record;
            try
            {
                record = JsonSerializer.Deserialize<T>(line, JsonOptions.Default);
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"Warning: Preserving malformed line in {Path.GetFileName(path)}: {ex.Message}");
                retained.Add(line);
                continue;
            }

            if (record is not null && shouldRemove(record))
            {
                removed++;
                continue;
            }

            retained.Add(line);
        }

        if (removed == 0)
        {
            Console.WriteLine($"No matching {label} records found in {path}");
            return 0;
        }

        if (retained.Count == 0)
            await File.WriteAllTextAsync(path, string.Empty, ct);
        else
            await File.WriteAllLinesAsync(path, retained, ct);

        Console.WriteLine($"Updated {path}");
        return removed;
    }
}
