namespace SmartCopy.Benchmarks;

internal sealed class DatasetPreparationService
{
    private readonly Random random;
    private readonly Func<DateTime> utcNow;


    public DatasetPreparationService(Random? random = null, Func<DateTime>? utcNow = null)
    {
        this.random = random ?? Random.Shared;
        this.utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public async Task<DatasetPreparationRunSummary> RunAsync(
        DatasetPreparationConfig config,
        string artifactDirectory,
        bool includeHidden,
        string? notes,
        IProgress<DatasetPreparationProgress>? progress,
        CancellationToken ct)
    {
        config.Normalize();
        ValidatePaths(config.SourcePath, config.DestinationPath);

        Directory.CreateDirectory(artifactDirectory);
        Directory.CreateDirectory(config.DestinationPath);

        var runStartedUtc = utcNow();
        var existingDataset = ScanExistingDataset(config, includeHidden, progress, ct);
        var existingRelativePaths = existingDataset.ExistingRelativePaths;
        var bucketStates = existingDataset.BucketStates;
        var beforeStates = CloneBucketStates(bucketStates);

        var duplicateSourceSkips = 0;
        var existingDestinationSkips = 0;
        var importedThisRun = new List<DatasetPreparationImportedFileRecord>();
        var candidatesByBucket = BuildCandidates(
            config,
            includeHidden,
            existingRelativePaths,
            ref duplicateSourceSkips,
            progress,
            ct);

        long totalBytesImported = 0;

        foreach (var bucket in config.Buckets)
        {
            var bucketState = bucketStates[bucket.Name];
            if (IsBucketFull(bucket, bucketState))
            {
                continue;
            }

            Shuffle(candidatesByBucket[bucket.Name]);

            foreach (var candidate in candidatesByBucket[bucket.Name])
            {
                ct.ThrowIfCancellationRequested();
                if (IsBucketFull(bucket, bucketState))
                {
                    break;
                }

                if (File.Exists(candidate.DestinationPath))
                {
                    existingDestinationSkips++;
                    continue;
                }

                try
                {
                    await CopyFileAsync(candidate.SourcePath, candidate.DestinationPath, ct);
                }
                catch (IOException) when (File.Exists(candidate.DestinationPath))
                {
                    existingDestinationSkips++;
                    continue;
                }

                var imported = new DatasetPreparationImportedFileRecord
                {
                    SourcePath = candidate.SourcePath,
                    RelativePath = candidate.RelativePath,
                    DestinationPath = candidate.DestinationPath,
                    BucketName = bucket.Name,
                    SizeBytes = candidate.SizeBytes,
                    ImportedUtc = utcNow(),
                };

                existingRelativePaths.Add(imported.RelativePath);
                importedThisRun.Add(imported);
                bucketState.ActualFileCount++;
                bucketState.ActualTotalBytes += imported.SizeBytes;
                totalBytesImported += imported.SizeBytes;

                progress?.Report(new DatasetPreparationProgress(
                    0, // BuildCandidates handles its own progress for scanning
                    importedThisRun.Count,
                    totalBytesImported,
                    imported.RelativePath));
            }
        }

        var runCompletedUtc = utcNow();
        var bucketProgress = BuildBucketProgress(config, beforeStates, bucketStates);
        var summaryPath = Path.Combine(
            artifactDirectory,
            $"dataset-prep-summary-{runStartedUtc:yyyyMMdd-HHmmss-fff}.json");
        var summary = new DatasetPreparationRunSummary
        {
            RunStartedUtc = runStartedUtc,
            RunCompletedUtc = runCompletedUtc,
            SourcePath = config.SourcePath,
            DestinationPath = config.DestinationPath,
            SummaryPath = summaryPath,
            Notes = notes,
            ImportedFileCount = importedThisRun.Count,
            ImportedTotalBytes = importedThisRun.Sum(f => f.SizeBytes),
            DuplicateSourceSkips = duplicateSourceSkips,
            ExistingDestinationSkips = existingDestinationSkips,
            ImportedFiles = importedThisRun,
            Buckets = bucketProgress,
        };

        await BenchmarkJson.WriteAsync(summaryPath, summary, ct);

        EnsurePoolClones(config.DestinationPath, config.PoolCloneCount, ct);

        return summary;
    }

    private static void EnsurePoolClones(string sourceDir, int cloneCount, CancellationToken ct)
    {
        if (cloneCount <= 0)
        {
            return;
        }

        Console.WriteLine($"\nEnsuring {cloneCount} dataset clones exist for pool rotation...");
        var normalizedSource = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 1; i <= cloneCount; i++)
        {
            var cloneDir = $"{normalizedSource}_{i}";
            Console.WriteLine($"  Verifying clone {i} of {cloneCount}: {cloneDir}...");
            CloneDirectoryIncremental(normalizedSource, cloneDir, ct);
        }
    }

    private static void CloneDirectoryIncremental(string sourceDir, string destinationDir, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            ct.ThrowIfCancellationRequested();
            var destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            var sourceInfo = new FileInfo(file);
            var destInfo = new FileInfo(destFile);

            if (!destInfo.Exists || destInfo.Length != sourceInfo.Length)
            {
                File.Copy(file, destFile, overwrite: true);
            }
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            var destDir = Path.Combine(destinationDir, Path.GetFileName(directory));
            CloneDirectoryIncremental(directory, destDir, ct);
        }
    }

    /// <summary>
    /// Replicates pool clones from the primary dataset location to each target path.
    /// Only clones (_1 through _N) are copied — the base dataset is not replicated
    /// since it is not included in path pools. Uses incremental cloning so
    /// interrupted runs can be safely resumed.
    /// </summary>
    internal static void ReplicatePoolClonesToTargets(
        string primaryBaseDir,
        int poolCloneCount,
        IReadOnlyList<string> targets,
        CancellationToken ct)
    {
        if (targets.Count == 0 || poolCloneCount <= 0)
            return;

        var normalizedPrimary = Path.GetFullPath(primaryBaseDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        Console.WriteLine();
        Console.WriteLine($"Replicating {poolCloneCount} pool clone(s) to {targets.Count} target drive(s)...");

        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            var normalizedTarget = Path.GetFullPath(target)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            for (var i = 1; i <= poolCloneCount; i++)
            {
                var targetClone = $"{normalizedTarget}_{i}";
                Console.WriteLine($"  [{normalizedTarget}] Cloning pool {i}/{poolCloneCount}...");
                CloneDirectoryIncremental(normalizedPrimary, targetClone, ct);
            }
        }

        Console.WriteLine("Pool clone replication complete.");
    }

    private static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken ct)
    {
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        await using var input = new FileStream(
            sourcePath,
            new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                BufferSize = 128 * 1024,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });

        await using var output = new FileStream(
            destinationPath,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 128 * 1024,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            });

        await input.CopyToAsync(output, 128 * 1024, ct);
    }


    private static Dictionary<string, List<DatasetCandidate>> BuildCandidates(
        DatasetPreparationConfig config,
        bool includeHidden,
        HashSet<string> existingRelativePaths,
        ref int duplicateSourceSkips,
        IProgress<DatasetPreparationProgress>? progress,
        CancellationToken ct)
    {
        var result = config.Buckets.ToDictionary(
            bucket => bucket.Name,
            _ => new List<DatasetCandidate>(),
            StringComparer.OrdinalIgnoreCase);

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };

        var scannedCount = 0;
        foreach (var path in Directory.EnumerateFiles(config.SourcePath, "*", options))
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(path);

            scannedCount++;
            if (scannedCount % 100 == 0)
                progress?.Report(new DatasetPreparationProgress(scannedCount, 0, 0, Path.GetRelativePath(config.SourcePath, fullPath)));

            if (!includeHidden && IsHidden(fullPath, config.SourcePath))
            {
                continue;
            }

            var info = new FileInfo(fullPath);
            var bucket = config.FindBucket(info.Length);
            if (bucket is null)
            {
                continue;
            }

            var relativePath = config.OrganizeByBucket
                ? Path.Combine(bucket.Name, Path.GetFileName(fullPath))
                : Path.GetRelativePath(config.SourcePath, fullPath);

            if (existingRelativePaths.Contains(relativePath))
            {
                duplicateSourceSkips++;
                continue;
            }

            // Reserve/deduplicate this destination path for this candidate during this prep run.
            existingRelativePaths.Add(relativePath);

            result[bucket.Name].Add(new DatasetCandidate(
                fullPath,
                relativePath,
                Path.Combine(config.DestinationPath, relativePath),
                info.Length));
        }

        return result;
    }

    private static ExistingDatasetScanResult ScanExistingDataset(
        DatasetPreparationConfig config,
        bool includeHidden,
        IProgress<DatasetPreparationProgress>? progress,
        CancellationToken ct)
    {
        var bucketStates = config.Buckets.ToDictionary(
            bucket => bucket.Name,
            bucket => new DatasetPreparationBucketState { BucketName = bucket.Name },
            StringComparer.OrdinalIgnoreCase);
        var existingRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };

        var scannedCount = 0;
        foreach (var path in Directory.EnumerateFiles(config.DestinationPath, "*", options))
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(path);

            scannedCount++;
            if (scannedCount % 100 == 0)
                progress?.Report(new DatasetPreparationProgress(scannedCount, 0, 0, Path.GetRelativePath(config.DestinationPath, fullPath)));

            if (!includeHidden && IsHidden(fullPath, config.DestinationPath))
            {
                continue;
            }

            var info = new FileInfo(fullPath);
            var bucket = config.FindBucket(info.Length);
            if (bucket is not null)
            {
                var state = bucketStates[bucket.Name];
                state.ActualFileCount++;
                state.ActualTotalBytes += info.Length;
            }

            existingRelativePaths.Add(Path.GetRelativePath(config.DestinationPath, fullPath));
        }

        return new ExistingDatasetScanResult(existingRelativePaths, bucketStates);
    }

    private static Dictionary<string, DatasetPreparationBucketState> CloneBucketStates(
        IReadOnlyDictionary<string, DatasetPreparationBucketState> bucketStates)
    {
        return bucketStates.ToDictionary(
            pair => pair.Key,
            pair => new DatasetPreparationBucketState
            {
                BucketName = pair.Value.BucketName,
                ActualFileCount = pair.Value.ActualFileCount,
                ActualTotalBytes = pair.Value.ActualTotalBytes,
            },
            StringComparer.OrdinalIgnoreCase);
    }

    private static List<DatasetPreparationBucketProgress> BuildBucketProgress(
        DatasetPreparationConfig config,
        IReadOnlyDictionary<string, DatasetPreparationBucketState> beforeStates,
        IReadOnlyDictionary<string, DatasetPreparationBucketState> afterStates)
    {
        var results = new List<DatasetPreparationBucketProgress>(config.Buckets.Count);

        foreach (var bucket in config.Buckets)
        {
            var before = beforeStates[bucket.Name];
            var after = afterStates[bucket.Name];
            results.Add(new DatasetPreparationBucketProgress
            {
                BucketName = bucket.Name,
                TargetTotalBytes = bucket.TargetTotalBytes,
                BeforeFileCount = before.ActualFileCount,
                BeforeTotalBytes = before.ActualTotalBytes,
                AddedFileCount = after.ActualFileCount - before.ActualFileCount,
                AddedTotalBytes = after.ActualTotalBytes - before.ActualTotalBytes,
                AfterFileCount = after.ActualFileCount,
                AfterTotalBytes = after.ActualTotalBytes,
                IsFull = IsBucketFull(bucket, after),
            });
        }

        return results;
    }

    private static bool IsBucketFull(DatasetPreparationBucketConfig bucket, DatasetPreparationBucketState state)
    {
        return state.ActualTotalBytes >= bucket.TargetTotalBytes;
    }

    private static bool IsHidden(string path, string rootPath)
    {
        var relative = Path.GetRelativePath(rootPath, path);
        var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => segment.StartsWith(".", StringComparison.Ordinal)))
        {
            return true;
        }

        return (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
    }

    private void Shuffle<T>(IList<T> values)
    {
        for (var i = values.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }

    private static void ValidatePaths(string sourcePath, string destinationPath)
    {
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException(sourcePath);
        }

        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Dataset prep source and destination paths must differ.");
        }

        if (IsSameOrNestedPath(sourcePath, destinationPath) || IsSameOrNestedPath(destinationPath, sourcePath))
        {
            throw new InvalidOperationException("Dataset prep source and destination paths must not be nested inside each other.");
        }
    }

    private static bool IsSameOrNestedPath(string parentPath, string childPath)
    {
        var normalizedParent = EnsureTrailingSeparator(Path.GetFullPath(parentPath));
        var normalizedChild = EnsureTrailingSeparator(Path.GetFullPath(childPath));
        return normalizedChild.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        if (path.Length > 0 &&
            (path[^1] == Path.DirectorySeparatorChar || path[^1] == Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }

    private sealed record DatasetCandidate(
        string SourcePath,
        string RelativePath,
        string DestinationPath,
        long SizeBytes);

    private sealed record ExistingDatasetScanResult(
        HashSet<string> ExistingRelativePaths,
        Dictionary<string, DatasetPreparationBucketState> BucketStates);
}
