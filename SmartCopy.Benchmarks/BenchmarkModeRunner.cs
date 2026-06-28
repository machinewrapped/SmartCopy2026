namespace SmartCopy.Benchmarks;

internal static class BenchmarkModeRunner
{
    private const string JournalDirectoryName = "benchmark-journals";

    public static async Task RunAsync(
        string workingDirectory,
        BenchmarkConfig config,
        BenchmarkCliOptions selection,
        CancellationToken ct)
    {
        var fileNames = FileNamesResolver.GetFileNames(selection.ConfigPath);
        var artifactDirectory = BenchmarkHelpers.ResolveArtifactDirectory(workingDirectory, config.SourcePath, config.ArtifactPath);
        var paths = new SessionPaths(
            ArtifactDirectory: artifactDirectory,
            ResultsPath: Path.Combine(artifactDirectory, fileNames.Results),
            FileResultsPath: Path.Combine(artifactDirectory, fileNames.FileResults),
            TaskListPath: Path.Combine(artifactDirectory, fileNames.TaskList),
            JournalDirectory: Path.Combine(artifactDirectory, JournalDirectoryName));

        Directory.CreateDirectory(artifactDirectory);

        var pass = new BenchmarkPass(workingDirectory, config, selection, paths, ct);
        await pass.ExecuteAsync();
    }

    internal static async Task ArchiveResultsAsync(string artifactDirectory, string configPath, CancellationToken ct)
    {
        var fileNames = FileNamesResolver.GetFileNames(configPath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var configName = Path.GetFileNameWithoutExtension(configPath);
        var archiveDirName = $"{timestamp}_{configName}";
        var archiveDirectory = GetUniqueArchiveDirectory(Path.Combine(artifactDirectory, "archive", archiveDirName));

        Directory.CreateDirectory(archiveDirectory);
        var copiedCount = 0;

        foreach (var src in EnumerateActiveArtifactFiles(artifactDirectory, fileNames))
        {
            ct.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(src);
            var dest = Path.Combine(archiveDirectory, fileName);
            try
            {
                File.Copy(src, dest, overwrite: true);
                copiedCount++;
                Console.WriteLine($"Archived file: {fileName}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error archiving {fileName}: {ex.Message}");
            }
        }

        var journalSrc = Path.Combine(artifactDirectory, JournalDirectoryName);
        if (Directory.Exists(journalSrc))
        {
            var journalDest = Path.Combine(archiveDirectory, JournalDirectoryName);
            try
            {
                CopyDirectory(journalSrc, journalDest, ct);
                copiedCount++;
                Console.WriteLine("Archived journals directory.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error archiving journals directory: {ex.Message}");
            }
        }

        if (copiedCount == 0)
        {
            Directory.Delete(archiveDirectory, recursive: true);
            Console.WriteLine("No benchmark artifacts found to archive.");
        }
        else
        {
            Console.WriteLine($"Archive snapshot: {archiveDirectory}");
        }

        await Task.CompletedTask;
    }

    internal static async Task ClearActiveArtifactsAsync(string artifactDirectory, string configPath, CancellationToken ct)
    {
        if (!Directory.Exists(artifactDirectory))
        {
            await Task.CompletedTask;
            return;
        }

        var fileNames = FileNamesResolver.GetFileNames(configPath);
        var deletedCount = 0;
        foreach (var file in EnumerateActiveArtifactFiles(artifactDirectory, fileNames))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                File.Delete(file);
                deletedCount++;
                Console.WriteLine($"Cleared active artifact: {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error clearing {Path.GetFileName(file)}: {ex.Message}");
            }
        }

        var journalDirectory = Path.Combine(artifactDirectory, JournalDirectoryName);
        if (Directory.Exists(journalDirectory))
        {
            try
            {
                Directory.Delete(journalDirectory, recursive: true);
                deletedCount++;
                Console.WriteLine("Cleared active journals directory.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error clearing journals directory: {ex.Message}");
            }
        }

        if (deletedCount == 0)
            Console.WriteLine("No active benchmark artifacts found to clear.");

        await Task.CompletedTask;
    }

    private static IEnumerable<string> EnumerateActiveArtifactFiles(string artifactDirectory, BenchmarkFileNames fileNames)
    {
        if (!Directory.Exists(artifactDirectory))
            yield break;

        var activeFileNames = new[]
        {
            fileNames.Results,
            fileNames.FileResults,
            fileNames.Analysis,
            fileNames.SizeScaling,
            fileNames.TaskList
        };

        foreach (var fileName in activeFileNames)
        {
            var path = Path.Combine(artifactDirectory, fileName);
            if (File.Exists(path))
                yield return path;
        }

        var baseAnalysisName = Path.GetFileNameWithoutExtension(fileNames.Analysis);
        foreach (var htmlFile in Directory.EnumerateFiles(artifactDirectory, $"{baseAnalysisName}*.html"))
            yield return htmlFile;
    }

    private static string GetUniqueArchiveDirectory(string baseArchiveDirectory)
    {
        if (!Directory.Exists(baseArchiveDirectory))
            return baseArchiveDirectory;

        var suffix = 2;
        string candidate;
        do
        {
            candidate = $"{baseArchiveDirectory}-{suffix}";
            suffix++;
        } while (Directory.Exists(candidate));

        return candidate;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, CancellationToken ct)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            ct.ThrowIfCancellationRequested();
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)), ct);
        }
    }
}
