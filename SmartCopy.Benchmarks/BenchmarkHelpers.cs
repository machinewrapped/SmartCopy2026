using System.Diagnostics;
using System.Text.Json;

namespace SmartCopy.Benchmarks;

internal static class BenchmarkHelpers
{
    public static double Percentile(IReadOnlyList<double> sortedValuesAscending, double percentile)
    {
        if (sortedValuesAscending.Count == 0)
        {
            return 0;
        }

        if (sortedValuesAscending.Count == 1)
        {
            return sortedValuesAscending[0];
        }

        var clamped = Math.Clamp(percentile, 0d, 1d);
        var index = (sortedValuesAscending.Count - 1) * clamped;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper)
        {
            return sortedValuesAscending[lower];
        }

        var weight = index - lower;
        return sortedValuesAscending[lower] + ((sortedValuesAscending[upper] - sortedValuesAscending[lower]) * weight);
    }

    public static string EscapeTable(string value) => value.Replace("|", "\\|");

    public static void UpdateProgress(string message)
    {
        // Use carriage return to overwrite the current line
        // ANSI escape code \x1b[K clears from cursor to end of line
        Console.Write($"\r{message}\x1b[K");
    }

    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < units.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {units[order]}";
    }

    public static string FormatDuration(TimeSpan duration)
    {
        if (duration == TimeSpan.Zero) return "0s";
        if (duration.TotalDays >= 1) return $"{(int)duration.TotalDays}d {duration.Hours}h";
        if (duration.TotalHours >= 1) return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        if (duration.TotalMinutes >= 1) return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{(int)duration.TotalSeconds}s";
    }

    public static string FormatBytesHuman(long bytes) => FormatSize(bytes);

    public static string FormatDurationHuman(double totalSeconds)
    {
        if (totalSeconds < 1.0) return $"{totalSeconds * 1000.0:0} ms";
        if (totalSeconds < 60.0) return $"{totalSeconds:0.0} sec";
        if (totalSeconds < 3600.0) return $"{(int)(totalSeconds / 60.0)}m {(int)(totalSeconds % 60.0)}s";
        return $"{(int)(totalSeconds / 3600.0)}h {((int)(totalSeconds % 3600.0) / 60)}m";
    }

    public static void ValidatePaths(string sourcePath, string destinationPath)
    {
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException(sourcePath);
        }

        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Source and destination paths must differ.");
        }

        if (IsSameOrNestedPath(sourcePath, destinationPath) || IsSameOrNestedPath(destinationPath, sourcePath))
        {
            throw new InvalidOperationException("Source and destination paths must not be nested inside each other.");
        }
    }

    public static bool IsSameOrNestedPath(string parentPath, string childPath)
    {
        var normalizedParent = EnsureTrailingSeparator(Path.GetFullPath(parentPath));
        var normalizedChild = EnsureTrailingSeparator(Path.GetFullPath(childPath));
        return normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    public static string EnsureTrailingSeparator(string path)
    {
        if (path.Length > 0 &&
            (path[^1] == Path.DirectorySeparatorChar || path[^1] == Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }

    public static string ResolveArtifactDirectory(string workingDirectory, string sourcePath, string? configuredArtifactPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredArtifactPath))
        {
            return Path.GetFullPath(configuredArtifactPath, workingDirectory);
        }

        var fullWorkingDirectory = Path.GetFullPath(workingDirectory);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (IsSameOrNestedPath(fullSourcePath, fullWorkingDirectory) || IsSameOrNestedPath(fullWorkingDirectory, fullSourcePath))
        {
            var sourceParent = Path.GetDirectoryName(fullSourcePath) ?? fullWorkingDirectory;
            var sourceLeaf = Path.GetFileName(fullSourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return Path.Combine(sourceParent, $"{sourceLeaf}-benchmark-artifacts");
        }

        return Path.Combine(fullWorkingDirectory, ".benchmarks");
    }

    public static void ClearDirectoryContents(string destinationPath, IProgress<string>? progress = null)
    {
        var fullDestinationPath = Path.GetFullPath(destinationPath);
        if (Path.GetPathRoot(fullDestinationPath)?.Equals(fullDestinationPath, StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new InvalidOperationException($"Refusing to clear drive root: {fullDestinationPath}");
        }

        if (!Directory.Exists(fullDestinationPath))
        {
            Directory.CreateDirectory(fullDestinationPath);
            return;
        }

        foreach (var file in Directory.EnumerateFiles(fullDestinationPath))
        {
            progress?.Report(Path.GetFileName(file));
            File.Delete(file);
        }

        foreach (var directory in Directory.EnumerateDirectories(fullDestinationPath))
        {
            progress?.Report(Path.GetFileName(directory));
            Directory.Delete(directory, recursive: true);
        }
    }

    public static async Task<List<T>> ReadExistingRunsAsync<T>(string resultsPath, CancellationToken ct)
    {
        var runs = new List<T>();
        if (!File.Exists(resultsPath))
        {
            return runs;
        }

        await foreach (var line in File.ReadLinesAsync(resultsPath, ct))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var run = JsonSerializer.Deserialize<T>(line, JsonOptions.Default);
                if (run is not null)
                {
                    runs.Add(run);
                }
            }
            catch (JsonException ex)
            {
                Console.Error.WriteLine($"Warning: Skipping malformed line in results file: {ex.Message}");
            }
        }

        return runs;
    }

    public static List<string> BuildScenarioOrder(BenchmarkConfig config, IEnumerable<string> scenarioNames)
    {
        var names = scenarioNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var configuredOrder = config.ScenarioExecutionOrder.Count > 0
            ? config.ScenarioExecutionOrder
            : ["SSDtoSSD", "SameDriveTest", "SSDtoHDD", "SSDtoUSBFlash"];

        var ordered = new List<string>();
        foreach (var configured in configuredOrder)
        {
            var match = names.FirstOrDefault(n => string.Equals(n, configured, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                ordered.Add(match);
            }
        }

        ordered.AddRange(names.Where(n => !ordered.Contains(n, StringComparer.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        return ordered;
    }

    public static bool IsRunForScenarioVariant(BenchmarkRunRecord run, string scenarioName, string variantName) =>
        string.Equals(run.ScenarioName, scenarioName, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(run.VariantName, variantName, StringComparison.OrdinalIgnoreCase);

    public static bool IsTerminalRun(BenchmarkRunRecord run) =>
        !string.Equals(run.RunStatus, BenchmarkRunStatus.InProgress, StringComparison.OrdinalIgnoreCase);

    public static bool IsTerminalRunForScenarioVariant(BenchmarkRunRecord run, string scenarioName, string variantName) =>
        IsRunForScenarioVariant(run, scenarioName, variantName) &&
        IsTerminalRun(run);

    public static bool IsSuccessfulRunForScenarioVariant(BenchmarkRunRecord run, string scenarioName, string variantName) =>
        IsRunForScenarioVariant(run, scenarioName, variantName) &&
        string.Equals(run.RunStatus, BenchmarkRunStatus.Completed, StringComparison.OrdinalIgnoreCase) &&
        run.FailedFiles == 0 &&
        run.ExceptionType is null;
}

internal static class PoolStateHelpers
{
    /// <summary>
    /// Loads an existing pool state from disk, or creates and persists a new shuffled state.
    /// Returns null if no scenarios use path pooling.
    /// </summary>
    public static async Task<PoolState?> LoadOrCreatePoolStateAsync(
        string artifactDirectory,
        BenchmarkConfig config,
        bool forceReshuffle,
        CancellationToken ct)
    {
        if (!config.Scenarios.Any(s => s.Enabled && s.UsePathPool))
        {
            return null;
        }

        var poolStatePath = Path.Combine(artifactDirectory, FileNamesResolver.PathPoolState);

        if (!forceReshuffle && File.Exists(poolStatePath))
        {
            try
            {
                var existing = await BenchmarkJson.ReadAsync<PoolState>(poolStatePath, ct);
                if (existing is not null && existing.ShuffledIndices.Count > 0)
                {
                    Console.WriteLine($"Loaded pool state: {existing.ShuffledIndices.Count} indices, position {existing.NextPosition}.");
                    return existing;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Could not read pool state file, creating new: {ex.Message}");
            }
        }

        // Discover pool indices from the filesystem
        var poolIndices = DiscoverPoolIndices(config);

        if (poolIndices.Count == 0)
        {
            int totalPoolRunsNeeded = config.Scenarios.Count(s => s.Enabled && s.UsePathPool)
                * config.Variants.Where(v => v.Enabled).Sum(v => v.DesiredRunCount);
            if (totalPoolRunsNeeded > 0)
            {
                poolIndices = Enumerable.Range(1, totalPoolRunsNeeded).ToList();
            }
        }

        if (poolIndices.Count == 0)
        {
            return null;
        }

        // Fisher-Yates shuffle
        var rand = new Random();
        for (var i = poolIndices.Count - 1; i > 0; i--)
        {
            var j = rand.Next(i + 1);
            (poolIndices[i], poolIndices[j]) = (poolIndices[j], poolIndices[i]);
        }

        var state = new PoolState
        {
            ShuffledIndices = poolIndices,
            NextPosition = 0,
        };

        await PersistAsync(state, artifactDirectory, ct);
        Console.WriteLine($"Created new pool state: {state.ShuffledIndices.Count} indices.");
        return state;
    }

    /// <summary>
    /// Increments the pool position and writes the updated state to disk.
    /// </summary>
    public static async Task AdvanceAndPersistAsync(PoolState state, string artifactDirectory, CancellationToken ct)
    {
        state.NextPosition++;
        await PersistAsync(state, artifactDirectory, ct);
    }

    /// <summary>
    /// Deletes the pool state file if it exists.
    /// </summary>
    public static Task DeletePoolStateAsync(string artifactDirectory)
    {
        var poolStatePath = Path.Combine(artifactDirectory, FileNamesResolver.PathPoolState);
        if (File.Exists(poolStatePath))
        {
            File.Delete(poolStatePath);
            Console.WriteLine("Deleted pool state file.");
        }

        return Task.CompletedTask;
    }

    private static async Task PersistAsync(PoolState state, string artifactDirectory, CancellationToken ct)
    {
        var poolStatePath = Path.Combine(artifactDirectory, FileNamesResolver.PathPoolState);
        await BenchmarkJson.WriteAsync(poolStatePath, state, ct);
    }

    private static List<int> DiscoverPoolIndices(BenchmarkConfig config)
    {
        var indices = new List<int>();
        var baseSource = Path.GetFullPath(config.SourcePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parentDir = Path.GetDirectoryName(baseSource);
        var baseName = Path.GetFileName(baseSource);

        if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
        {
            return indices;
        }

        var prefix = baseName + "_";
        foreach (var dir in Directory.EnumerateDirectories(parentDir))
        {
            var dirName = Path.GetFileName(dir);
            if (dirName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(dirName.AsSpan(prefix.Length), out var index))
            {
                indices.Add(index);
            }
        }

        return indices;
    }
}

internal sealed class ThrottledConsoleProgress<T> : IProgress<T>, IDisposable
{
    private readonly Action<T> _handler;
    private readonly TimeSpan _throttle;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private T? _lastValue;
    private bool _hasValue;

    public ThrottledConsoleProgress(Action<T> handler, TimeSpan? throttle = null)
    {
        _handler = handler;
        _throttle = throttle ?? TimeSpan.FromMilliseconds(100);
    }

    public void Report(T value)
    {
        _lastValue = value;
        _hasValue = true;

        if (_stopwatch.Elapsed >= _throttle)
        {
            _handler(value);
            _stopwatch.Restart();
            _hasValue = false;
        }
    }

    public void Dispose()
    {
        if (_hasValue && _lastValue != null)
        {
            _handler(_lastValue);
        }
    }
}
