namespace SmartCopy.Core.FileSystem;

/// <summary>Selects the byte-pump path in <see cref="StreamCopyEngine"/>.</summary>
public enum LocalFileSystemWriteMode
{
    /// <summary>Choose per file from size and whether progress is wired.</summary>
    Auto,
    /// <summary>Force the per-chunk loop (reports progress per chunk).</summary>
    ManualLoop,
    /// <summary>Force the framework <c>Stream.CopyToAsync</c>.</summary>
    CopyToAsync,
}

/// <summary>Per-write durability intent, decided by the copy strategy and executed by the provider
/// (full design: <c>Docs/Architecture.md</c> §2.4.1).</summary>
public enum WriteDurability
{
    /// <summary>Crash-safe commit; the provider picks the mechanism (e.g. temp file + atomic rename).</summary>
    Staged,
    /// <summary>Write straight to the destination, no staging — used for tiny files and for providers
    /// that cannot stage (<see cref="ProviderCapabilities.AllowStagedWrite"/> = false).</summary>
    Direct,
}

public sealed record OperationalSettings
{
    public const int DefaultCopyBufferSizeBytes = 256 * 1024;
    public const long DefaultSmallFileProgressThresholdBytes = 10L * 1024 * 1024;
    public const long DefaultBatchEligibilityCeilingBytes = 512 * 1024;
    public const int DefaultCompletionProgressIntervalMs = 100;
    public const int DefaultCompletionProgressBatchFiles = 100;

    public const long DefaultEnabledTinyFileFastPathThresholdBytes = 256 * 1024;
    public const long DefaultEnabledBatchBufferBytes = 1024 * 1024;

    /// <summary>Size of the per-file copy buffer (and the ArrayPool rent size for the manual loop).</summary>
    public int CopyBufferSizeBytes { get; init; } = DefaultCopyBufferSizeBytes;
    /// <summary>Files at or below this size report progress once on completion rather than per chunk.</summary>
    public long SmallFileProgressThresholdBytes { get; init; } = DefaultSmallFileProgressThresholdBytes;
    /// <summary>Which byte-pump path the engine uses; <see cref="LocalFileSystemWriteMode.Auto"/> decides per file.</summary>
    public LocalFileSystemWriteMode WriteMode { get; init; } = LocalFileSystemWriteMode.Auto;
    /// <summary>Rent the manual-loop buffer from <c>ArrayPool</c> instead of allocating one per file.</summary>
    public bool UseArrayPoolForManualLoop { get; init; } = true;
    /// <summary>Files at or below this size are written <see cref="WriteDurability.Direct"/> (no staging).
    /// <c>0</c> disables the fast path (always stage).</summary>
    public long TinyFileFastPathThresholdBytes { get; init; }
    /// <summary>When &gt; 0, selects <c>BatchedCopyStrategy</c> and sets its accumulation-buffer capacity;
    /// <c>0</c> uses <c>StreamingCopyStrategy</c>.</summary>
    public long BatchBufferBytes { get; init; }
    /// <summary>Files at or below this size batch; larger ones stream individually. Capped to the batch
    /// buffer capacity, so the default 512 KiB keeps ≥2 files per flush of a 1 MiB+ buffer — which is
    /// what makes phase separation happen. <c>0</c> disables the ceiling (use buffer capacity).</summary>
    public long BatchEligibilityCeilingBytes { get; init; } = DefaultBatchEligibilityCeilingBytes;
    /// <summary>When true, batch traversal copies each directory's selected files in ascending size
    /// order for better buffer packing. When false, preserves the directory tree's natural file order.</summary>
    public bool BatchOrderByFileSize { get; init; } = true;
    /// <summary>Per-file durability intent (set by the strategy). Default <see cref="WriteDurability.Staged"/>
    /// keeps direct <c>WriteAsync</c> callers crash-safe.</summary>
    public WriteDurability WriteDurability { get; init; } = WriteDurability.Staged;
    /// <summary>When true, the copy strategy policy selects the copy buffer from the
    /// source→destination drive pair. Default false preserves the fixed-buffer behaviour.</summary>
    public bool DestinationRoutingEnabled { get; init; }
    /// <summary>Settings-backed copy-buffer routing profile used when destination routing is enabled.</summary>
    public CopyBufferRoutingSettings CopyBufferRouting { get; init; } = new();
    /// <summary>Minimum interval between completion-progress reports, in milliseconds. 0 disables the time gate.</summary>
    public int CompletionProgressIntervalMs { get; init; } = DefaultCompletionProgressIntervalMs;
    /// <summary>Minimum number of files between completion-progress reports. 0 disables the file-count gate.</summary>
    public int CompletionProgressBatchFiles { get; init; } = DefaultCompletionProgressBatchFiles;

    public OperationalSettings Normalize()
    {
        if (CopyBufferSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CopyBufferSizeBytes), "Copy buffer size must be positive.");
        }

        if (SmallFileProgressThresholdBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SmallFileProgressThresholdBytes),
                "Small-file progress threshold must be zero or greater.");
        }

        if (!Enum.IsDefined(WriteMode))
        {
            throw new ArgumentOutOfRangeException(nameof(WriteMode), "Write mode must be a defined value.");
        }

        if (TinyFileFastPathThresholdBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TinyFileFastPathThresholdBytes),
                "Tiny-file fast-path threshold must be zero or greater.");
        }

        if (BatchBufferBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BatchBufferBytes),
                "Batch buffer size must be zero or greater.");
        }

        // The batch buffer is rented as a single array via ArrayPool<byte>.Rent((int)capacity); a value
        // above int.MaxValue would overflow the cast and fault at runtime — fail fast with a clear error.
        if (BatchBufferBytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BatchBufferBytes),
                "Batch buffer size must not exceed Int32.MaxValue.");
        }

        if (BatchEligibilityCeilingBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(BatchEligibilityCeilingBytes),
                "Batch eligibility ceiling must be zero or greater.");
        }

        if (CompletionProgressIntervalMs < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CompletionProgressIntervalMs),
                "Completion progress interval must be zero or greater.");
        }

        if (CompletionProgressBatchFiles < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CompletionProgressBatchFiles),
                "Completion progress batch size must be zero or greater.");
        }

        return this with { CopyBufferRouting = CopyBufferRouting.Normalize() };
    }

    /// <summary>
    /// Returns a copy of these settings clamped to the capabilities of the source and target providers.
    /// Disables batching if either provider does not support it, or if both impose a max buffer limit.
    /// </summary>
    public OperationalSettings WithProviderConstraints(ProviderCapabilities source, ProviderCapabilities target)
    {
        var batchBytes = BatchBufferBytes;
        if (source.MaxBatchBufferBytes > 0) batchBytes = Math.Min(batchBytes, source.MaxBatchBufferBytes);
        if (target.MaxBatchBufferBytes > 0) batchBytes = Math.Min(batchBytes, target.MaxBatchBufferBytes);
        if (!source.AllowBatchRead || !target.AllowBatchWrite) batchBytes = 0;
        return this with { BatchBufferBytes = batchBytes };
    }
}
