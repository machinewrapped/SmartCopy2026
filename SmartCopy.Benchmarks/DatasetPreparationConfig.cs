namespace SmartCopy.Benchmarks;

internal sealed class DatasetPreparationConfig
{
    public string SourcePath { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public bool OrganizeByBucket { get; set; }
    public int PoolCloneCount { get; set; }
    public List<DatasetPreparationBucketConfig> Buckets { get; set; } = [];

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            throw new InvalidOperationException("datasetPreparation.sourcePath is required.");
        }

        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            throw new InvalidOperationException("datasetPreparation.destinationPath is required.");
        }

        SourcePath = Path.GetFullPath(SourcePath);
        DestinationPath = Path.GetFullPath(DestinationPath);

        if (Buckets.Count == 0)
        {
            throw new InvalidOperationException("datasetPreparation.buckets must contain at least one bucket.");
        }

        if (PoolCloneCount < 0)
        {
            throw new InvalidOperationException("datasetPreparation.poolCloneCount cannot be negative.");
        }

        foreach (var bucket in Buckets)
        {
            bucket.Normalize();
        }

        var duplicateName = Buckets
            .GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicateName is not null)
        {
            throw new InvalidOperationException($"datasetPreparation bucket name '{duplicateName.Key}' is duplicated.");
        }

        var orderedBuckets = Buckets.OrderBy(b => b.MinimumFileSizeBytes).ToList();
        for (var i = 1; i < orderedBuckets.Count; i++)
        {
            if (orderedBuckets[i].MinimumFileSizeBytes <= orderedBuckets[i - 1].MaximumFileSizeBytes)
            {
                throw new InvalidOperationException(
                    $"datasetPreparation bucket '{orderedBuckets[i].Name}' overlaps '{orderedBuckets[i - 1].Name}'.");
            }
        }
    }

    public DatasetPreparationBucketConfig? FindBucket(long sizeBytes) =>
        Buckets.FirstOrDefault(b => b.Contains(sizeBytes));

}

internal sealed class DatasetPreparationBucketConfig
{
    public string Name { get; set; } = string.Empty;
    public long MinimumFileSizeBytes { get; set; }
    public long MaximumFileSizeBytes { get; set; }
    public long TargetTotalBytes { get; set; }

    public void Normalize()
    {
        Name = Name.Trim();
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("datasetPreparation bucket name is required.");
        }

        if (MinimumFileSizeBytes < 0)
        {
            throw new InvalidOperationException($"datasetPreparation bucket '{Name}' has a negative minimum size.");
        }

        if (MaximumFileSizeBytes < MinimumFileSizeBytes)
        {
            throw new InvalidOperationException($"datasetPreparation bucket '{Name}' has max size below min size.");
        }

        if (TargetTotalBytes <= 0)
        {
            throw new InvalidOperationException($"datasetPreparation bucket '{Name}' targetTotalBytes must be positive.");
        }
    }

    public bool Contains(long sizeBytes) =>
        sizeBytes >= MinimumFileSizeBytes && sizeBytes <= MaximumFileSizeBytes;

    public bool Matches(DatasetPreparationBucketConfig other) =>
        string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) &&
        MinimumFileSizeBytes == other.MinimumFileSizeBytes &&
        MaximumFileSizeBytes == other.MaximumFileSizeBytes &&
        TargetTotalBytes == other.TargetTotalBytes;
}

internal sealed class DatasetPreparationBucketState
{
    public string BucketName { get; set; } = string.Empty;
    public int ActualFileCount { get; set; }
    public long ActualTotalBytes { get; set; }
}

internal sealed class DatasetPreparationImportedFileRecord
{
    public required string SourcePath { get; init; }
    public required string RelativePath { get; init; }
    public required string DestinationPath { get; init; }
    public required string BucketName { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTime ImportedUtc { get; init; }
}

internal sealed class DatasetPreparationRunSummary
{
    public required DateTime RunStartedUtc { get; init; }
    public required DateTime RunCompletedUtc { get; init; }
    public required string SourcePath { get; init; }
    public required string DestinationPath { get; init; }
    public required string SummaryPath { get; init; }
    public string? Notes { get; init; }
    public required int ImportedFileCount { get; init; }
    public required long ImportedTotalBytes { get; init; }
    public required int DuplicateSourceSkips { get; init; }
    public required int ExistingDestinationSkips { get; init; }
    public required List<DatasetPreparationImportedFileRecord> ImportedFiles { get; init; }
    public required List<DatasetPreparationBucketProgress> Buckets { get; init; }
}

internal sealed class DatasetPreparationBucketProgress
{
    public required string BucketName { get; init; }
    public required long TargetTotalBytes { get; init; }
    public required int BeforeFileCount { get; init; }
    public required long BeforeTotalBytes { get; init; }
    public required int AddedFileCount { get; init; }
    public required long AddedTotalBytes { get; init; }
    public required int AfterFileCount { get; init; }
    public required long AfterTotalBytes { get; init; }
    public required bool IsFull { get; init; }
}
