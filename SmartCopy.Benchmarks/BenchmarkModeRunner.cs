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
        var archiveDirectory = Path.Combine(artifactDirectory, "archive", archiveDirName);

        Directory.CreateDirectory(archiveDirectory);
        Console.WriteLine($"Created archive directory: {archiveDirectory}");

        var filesToMove = new[]
        {
            fileNames.Results,
            fileNames.FileResults,
            fileNames.Analysis,
            fileNames.TaskList
        };

        foreach (var fileName in filesToMove)
        {
            var src = Path.Combine(artifactDirectory, fileName);
            if (File.Exists(src))
            {
                var dest = Path.Combine(archiveDirectory, fileName);
                try
                {
                    File.Move(src, dest, overwrite: true);
                    Console.WriteLine($"Archived file: {fileName}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error archiving {fileName}: {ex.Message}");
                }
            }
        }

        var baseAnalysisName = Path.GetFileNameWithoutExtension(fileNames.Analysis);
        var htmlFiles = Directory.GetFiles(artifactDirectory, $"{baseAnalysisName}*.html");
        foreach (var htmlFile in htmlFiles)
        {
            var fileName = Path.GetFileName(htmlFile);
            var dest = Path.Combine(archiveDirectory, fileName);
            try
            {
                File.Move(htmlFile, dest, overwrite: true);
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
                Directory.Move(journalSrc, journalDest);
                Console.WriteLine("Archived journals directory.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error archiving journals directory: {ex.Message}");
            }
        }

        await Task.CompletedTask;
    }
}
