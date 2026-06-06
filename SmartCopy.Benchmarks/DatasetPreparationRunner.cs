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

        // Deduce replication targets from scenario source paths.
        // Any path-pool scenario whose effective source differs from the primary
        // dataset location needs pool clones replicated to that drive.
        if (preparation.PoolCloneCount > 0)
        {
            var primaryBase = Path.GetFullPath(preparation.DestinationPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            var replicationTargets = config.Scenarios
                .Where(s => s.Enabled && s.UsePathPool)
                .Select(s => Path.GetFullPath(
                    !string.IsNullOrWhiteSpace(s.SourcePath) ? s.SourcePath : config.SourcePath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Where(p => !string.Equals(p, primaryBase, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            DatasetPreparationService.ReplicatePoolClonesToTargets(
                primaryBase, preparation.PoolCloneCount, replicationTargets, ct);
        }
    }
}
