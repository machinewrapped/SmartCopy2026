namespace SmartCopy.Core.FileSystem;

public enum LocalFileSystemWriteMode
{
    Auto,
    ManualLoop,
    CopyToAsync,
}

/// <summary>
/// Durability intent for a single write, decided by the copy strategy and honoured by the provider.
/// <see cref="Staged"/> requests a crash-safe commit (the provider chooses the mechanism — e.g. a temp
/// file + atomic rename); <see cref="Direct"/> permits writing straight to the destination, trading
/// interruption-safety for less per-file ceremony (used for tiny files where the write is effectively
/// atomic anyway). A provider that cannot stage (<see cref="ProviderCapabilities.AllowStagedWrite"/> =
/// false) is always asked for <see cref="Direct"/>.
/// </summary>
public enum WriteDurability
{
    Staged,
    Direct,
}

public sealed record OperationalSettings
{
    public int CopyBufferSizeBytes { get; init; } = 256 * 1024;
    public long SmallFileProgressThresholdBytes { get; init; } = 10L * 1024 * 1024;
    public LocalFileSystemWriteMode WriteMode { get; init; } = LocalFileSystemWriteMode.Auto;
    public bool UseArrayPoolForManualLoop { get; init; }
    public bool PreallocateDestinationFile { get; init; }
    public long TinyFileFastPathThresholdBytes { get; init; }
    public long BatchBufferBytes { get; init; }
    /// <summary>
    /// Files at or below this size are eligible for the batch path; larger files bypass batching and
    /// stream individually (the ManualLoop path). Capped to the batch buffer capacity where used, so
    /// eligibility alone guarantees a file fits the buffer. Default 512 KiB ensures genuine phase
    /// separation — at least two files share every flush of a 1 MiB+ buffer (see
    /// <c>Docs/optimisation-strategies.md</c> Phase 3). <c>0</c> disables the ceiling (use buffer capacity).
    /// </summary>
    public long BatchEligibilityCeilingBytes { get; init; } = 512 * 1024;
    /// <summary>
    /// Durability intent for the write. Set per file by the copy strategy (from the tiny-file
    /// threshold and the target's staging capability) and honoured by the provider. Default
    /// <see cref="WriteDurability.Staged"/> preserves crash-safe behaviour for direct WriteAsync callers.
    /// </summary>
    public WriteDurability WriteDurability { get; init; } = WriteDurability.Staged;
    /// <summary>When true, the copy strategy policy selects the copy buffer from the
    /// source→destination drive pair. Default false preserves the fixed-buffer behaviour.</summary>
    public bool DestinationRoutingEnabled { get; init; }
    /// <summary>Minimum interval between completion-progress reports, in milliseconds. 0 disables the time gate.</summary>
    public int CompletionProgressIntervalMs { get; init; } = 100;
    /// <summary>Minimum number of files between completion-progress reports. 0 disables the file-count gate.</summary>
    public int CompletionProgressBatchFiles { get; init; } = 100;

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

        return this;
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
