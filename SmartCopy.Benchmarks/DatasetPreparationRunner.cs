namespace SmartCopy.Benchmarks;

internal static class DatasetPreparationRunner
{
    public static async Task RunAsync(
        string workingDirectory,
        BenchmarkConfig config,
        BenchmarkCliOptions selection,
        CancellationToken ct)
    {
        var preparation = config.DatasetPreparation
            ?? throw new InvalidOperationException("Benchmark Scenario does not define datasetPreparation.");

        var artifactDirectory = BenchmarkHelpers.ResolveArtifactDirectory(workingDirectory, preparation.SourcePath, config.ArtifactPath);

        Console.WriteLine("Mode:     dataset-prep");
        Console.WriteLine($"Source:   {preparation.SourcePath}");
        Console.WriteLine($"Dataset:  {preparation.DestinationPath}");
        Console.WriteLine($"Artifacts:{artifactDirectory}");

        var service = new DatasetPreparationService();
        DatasetPreparationRunSummary summary;

        using (var progress = new ThrottledConsoleProgress<DatasetPreparationProgress>(p =>
            BenchmarkHelpers.UpdateProgress($"Prep: Scanned={p.TotalFilesScanned}, Imported={p.TotalFilesImported} ({BenchmarkHelpers.FormatSize(p.TotalBytesImported)}), File={p.CurrentFile}")))
        {
            summary = await service.RunAsync(preparation, artifactDirectory, config.IncludeHidden, selection.Notes, progress, ct);
        }
        Console.WriteLine(); // Finalize progress line

        Console.WriteLine($"Imported files: {summary.ImportedFileCount}, bytes: {summary.ImportedTotalBytes}.");
        Console.WriteLine($"Skipped duplicates: {summary.DuplicateSourceSkips}, skipped conflicts: {summary.ExistingDestinationSkips}.");
        Console.WriteLine($"Summary:  {summary.SummaryPath}");

        foreach (var bucket in summary.Buckets)
        {
            var status = bucket.IsFull ? "full" : "underfilled";
            Console.WriteLine(
                $"Bucket {bucket.BucketName}: +{bucket.AddedFileCount} files / +{bucket.AddedTotalBytes} bytes, " +
                $"now {bucket.AfterFileCount} files / {bucket.AfterTotalBytes} bytes ({status}).");
        }

        int totalRunsNeeded = config.Scenarios.Count(s => s.Enabled && s.UsePathPool)
            * config.Variants.Where(v => v.Enabled).Sum(v => v.DesiredRunCount);
        if (totalRunsNeeded > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Duplicating dataset {totalRunsNeeded} times for caching resistance...");
            var baseDest = preparation.DestinationPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            for (int i = 1; i <= totalRunsNeeded; i++)
            {
                var targetPath = $"{baseDest}_{i:D2}";
                Console.WriteLine($"[{i:D2}/{totalRunsNeeded:D2}] Copying base dataset to {targetPath}...");
                DuplicateDirectory(baseDest, targetPath);
            }
            Console.WriteLine("Dataset duplication complete.");
        }
    }

    private static void DuplicateDirectory(string sourceDir, string destDir)
    {
        var source = new DirectoryInfo(sourceDir);
        if (!source.Exists)
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
        }

        if (Directory.Exists(destDir))
        {
            var fullDest = Path.GetFullPath(destDir);
            if (Path.GetPathRoot(fullDest)?.Equals(fullDest, StringComparison.OrdinalIgnoreCase) != true)
            {
                Directory.Delete(fullDest, recursive: true);
            }
        }

        Directory.CreateDirectory(destDir);

        foreach (var dir in source.GetDirectories("*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, dir.FullName);
            Directory.CreateDirectory(Path.Combine(destDir, relativePath));
        }

        foreach (var file in source.GetFiles("*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, file.FullName);
            var destFile = Path.Combine(destDir, relativePath);
            file.CopyTo(destFile, overwrite: true);
        }
    }
}
