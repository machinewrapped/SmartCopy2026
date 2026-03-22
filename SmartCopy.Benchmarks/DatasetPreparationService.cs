namespace SmartCopy.Benchmarks;

internal sealed class DatasetPreparationService
{
    private const string ManifestFileName = "dataset-prep-manifest.json";
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
        CancellationToken ct)
    {
        config.Normalize();
        ValidatePaths(config.SourcePath, config.DestinationPath);

        Directory.CreateDirectory(artifactDirectory);
        Directory.CreateDirectory(config.DestinationPath);

        var manifestPath = Path.Combine(artifactDirectory, ManifestFileName);
        var runStartedUtc = utcNow();
        var manifest = await LoadManifestAsync(config, manifestPath, ct);
        ValidateManifestFilesExist(manifest);

        var importedSources = new HashSet<string>(
            manifest.ImportedFiles.Select(f => Path.GetFullPath(f.SourcePath)),
            StringComparer.OrdinalIgnoreCase);
        var bucketStates = manifest.BucketStates.ToDictionary(s => s.BucketName, StringComparer.OrdinalIgnoreCase);
        var beforeStates = manifest.BucketStates.ToDictionary(
            s => s.BucketName,
            s => new DatasetPreparationBucketState
            {
                BucketName = s.BucketName,
                ActualFileCount = s.ActualFileCount,
                ActualTotalBytes = s.ActualTotalBytes,
            },
            StringComparer.OrdinalIgnoreCase);

        var duplicateSourceSkips = 0;
        var existingDestinationSkips = 0;
        var importedThisRun = new List<DatasetPreparationImportedFileRecord>();
        var candidatesByBucket = BuildCandidates(
            config,
            includeHidden,
            importedSources,
            ref duplicateSourceSkips,
            ct);

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

                importedSources.Add(imported.SourcePath);
                importedThisRun.Add(imported);
                manifest.ImportedFiles.Add(imported);
                bucketState.ActualFileCount++;
                bucketState.ActualTotalBytes += imported.SizeBytes;
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
            ManifestPath = manifestPath,
            SummaryPath = summaryPath,
            Notes = notes,
            ImportedFileCount = importedThisRun.Count,
            ImportedTotalBytes = importedThisRun.Sum(f => f.SizeBytes),
            DuplicateSourceSkips = duplicateSourceSkips,
            ExistingDestinationSkips = existingDestinationSkips,
            ImportedFiles = importedThisRun,
            Buckets = bucketProgress,
        };

        manifest.LastUpdatedUtc = runCompletedUtc;
        manifest.Runs.Add(new DatasetPreparationManifestRunRecord
        {
            RunStartedUtc = runStartedUtc,
            RunCompletedUtc = runCompletedUtc,
            SourcePath = config.SourcePath,
            Notes = notes,
            ImportedFileCount = summary.ImportedFileCount,
            ImportedTotalBytes = summary.ImportedTotalBytes,
            DuplicateSourceSkips = duplicateSourceSkips,
            ExistingDestinationSkips = existingDestinationSkips,
            Buckets = bucketProgress,
        });

        await BenchmarkJson.WriteAsync(manifestPath, manifest, ct);
        await BenchmarkJson.WriteAsync(summaryPath, summary, ct);
        return summary;
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
        HashSet<string> importedSources,
        ref int duplicateSourceSkips,
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

        foreach (var path in Directory.EnumerateFiles(config.SourcePath, "*", options))
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(path);
            if (!includeHidden && IsHidden(fullPath, config.SourcePath))
            {
                continue;
            }

            if (importedSources.Contains(fullPath))
            {
                duplicateSourceSkips++;
                continue;
            }

            var info = new FileInfo(fullPath);
            var bucket = config.FindBucket(info.Length);
            if (bucket is null)
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(config.SourcePath, fullPath);
            result[bucket.Name].Add(new DatasetCandidate(
                fullPath,
                relativePath,
                Path.Combine(config.DestinationPath, relativePath),
                info.Length));
        }

        return result;
    }

    private async Task<DatasetPreparationManifest> LoadManifestAsync(
        DatasetPreparationConfig config,
        string manifestPath,
        CancellationToken ct)
    {
        if (!File.Exists(manifestPath))
        {
            return new DatasetPreparationManifest
            {
                DestinationPath = config.DestinationPath,
                Buckets = config.Buckets.Select(CloneBucket).ToList(),
                BucketStates = config.Buckets
                    .Select(b => new DatasetPreparationBucketState { BucketName = b.Name })
                    .ToList(),
                LastUpdatedUtc = utcNow(),
            };
        }

        var manifest = await BenchmarkJson.ReadAsync<DatasetPreparationManifest>(manifestPath, ct)
            ?? throw new InvalidOperationException($"Could not read dataset manifest at {manifestPath}.");

        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidOperationException($"Unsupported dataset manifest schema version {manifest.SchemaVersion}.");
        }

        if (!config.IsCompatibleWith(manifest))
        {
            throw new InvalidOperationException(
                "Existing dataset manifest is incompatible with the current datasetPreparation destination or bucket definitions.");
        }

        foreach (var bucket in config.Buckets)
        {
            if (!manifest.BucketStates.Any(s => string.Equals(s.BucketName, bucket.Name, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException(
                    $"Existing dataset manifest does not contain state for bucket '{bucket.Name}'.");
            }
        }

        return manifest;
    }

    private static void ValidateManifestFilesExist(DatasetPreparationManifest manifest)
    {
        foreach (var imported in manifest.ImportedFiles)
        {
            if (!File.Exists(imported.DestinationPath))
            {
                throw new InvalidOperationException(
                    $"Dataset manifest references a missing destination file: {imported.DestinationPath}");
            }
        }
    }

    private static DatasetPreparationBucketConfig CloneBucket(DatasetPreparationBucketConfig bucket)
    {
        return new DatasetPreparationBucketConfig
        {
            Name = bucket.Name,
            MinimumFileSizeBytes = bucket.MinimumFileSizeBytes,
            MaximumFileSizeBytes = bucket.MaximumFileSizeBytes,
            TargetFileCount = bucket.TargetFileCount,
            TargetTotalBytes = bucket.TargetTotalBytes,
        };
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
                TargetFileCount = bucket.TargetFileCount,
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
        if (bucket.TargetFileCount is int targetFileCount && state.ActualFileCount >= targetFileCount)
        {
            return true;
        }

        if (bucket.TargetTotalBytes is long targetTotalBytes && state.ActualTotalBytes >= targetTotalBytes)
        {
            return true;
        }

        return false;
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
}
