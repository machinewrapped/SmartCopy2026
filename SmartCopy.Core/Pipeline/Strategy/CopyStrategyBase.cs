using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Progress;

namespace SmartCopy.Core.Pipeline.Strategy;

/// <summary>
/// Shared byte-transfer mechanics for copy strategies: single-file transfer, destination-result
/// resolution, progress wiring, and result shaping. Subclasses implement
/// <see cref="ICopyStrategy.CopySelectionAsync"/> with their own batching behaviour.
/// </summary>
public abstract class CopyStrategyBase : ICopyStrategy
{
    public OperationalSettings Settings { get; }

    // Durability is binary, so the two settings variants are precomputed once per strategy (one alloc
    // each) rather than allocated per file via `with`. If per-file write parameters ever become
    // combinatorial, this precompute stops scaling — switch to a per-call argument at that point.
    private readonly OperationalSettings _stagedSettings;
    private readonly OperationalSettings _directSettings;
    private readonly bool _targetSupportsStaging;
    private readonly long _tinyThreshold;

    protected CopyStrategyBase(OperationalSettings settings, bool targetSupportsStaging)
    {
        Settings = settings;
        _targetSupportsStaging = targetSupportsStaging;
        _tinyThreshold = settings.TinyFileFastPathThresholdBytes;
        _stagedSettings = settings with { WriteDurability = WriteDurability.Staged };
        _directSettings = settings with { WriteDurability = WriteDurability.Direct };
    }

    /// <summary>
    /// Selects the settings variant carrying the durability intent for a file of <paramref name="fileSize"/>:
    /// Direct when the target cannot stage, or when the file is at/below the tiny-file threshold; Staged otherwise.
    /// </summary>
    internal OperationalSettings SettingsFor(long fileSize)
    {
        if (!_targetSupportsStaging)
            return _directSettings;
        return _tinyThreshold > 0 && fileSize <= _tinyThreshold ? _directSettings : _stagedSettings;
    }

    /// <summary>
    /// Formats the resolved mechanics for the preview. Batching presence is read from the settings
    /// (batch buffer &gt; 0 ⇒ batched), and durability reflects what <see cref="SettingsFor"/> will
    /// choose per file: direct when the target cannot stage, otherwise staged with a direct fast-path
    /// for files at/below the tiny-file threshold.
    /// </summary>
    public string Describe()
    {
        var batched = Settings.BatchBufferBytes > 0;
        var kind = batched ? "Batched copy" : "Streaming copy";
        // For batched copies, surface both the buffer (how much accumulates per flush) and the
        // eligibility ceiling (which files batch vs stream individually).
        var batch = batched
            ? $" · {FormatBytes(Settings.BatchBufferBytes)} batches (≤ {FormatBytes(EffectiveBatchCeiling())} eligible)"
            : "";
        return $"{kind} · {FormatBytes(Settings.CopyBufferSizeBytes)} buffer{batch} · {DescribeDurability()}";
    }

    /// <summary>The batch eligibility ceiling actually applied: the configured value clamped to the
    /// buffer capacity, or the capacity itself when the ceiling is disabled (0).</summary>
    private long EffectiveBatchCeiling()
    {
        var ceiling = Settings.BatchEligibilityCeilingBytes;
        return ceiling <= 0 ? Settings.BatchBufferBytes : Math.Min(ceiling, Settings.BatchBufferBytes);
    }

    private string DescribeDurability()
    {
        // Unconditional cases read as plain "direct writes" / "staged writes"; only a mixed policy
        // (staging with a tiny-file direct fast-path) needs the qualifier.
        if (!_targetSupportsStaging)
            return "direct writes";
        return _tinyThreshold > 0
            ? $"staged writes (direct ≤ {FormatBytes(_tinyThreshold)})"
            : "staged writes";
    }

    /// <summary>Compact binary-unit size for display: whole MiB/KiB where exact, else one decimal.</summary>
    internal static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            var mib = bytes / (1024.0 * 1024.0);
            return mib == Math.Floor(mib) ? $"{(long)mib} MiB" : $"{mib:0.#} MiB";
        }
        if (bytes >= 1024)
        {
            var kib = bytes / 1024.0;
            return kib == Math.Floor(kib) ? $"{(long)kib} KiB" : $"{kib:0.#} KiB";
        }
        return $"{bytes} B";
    }

    public abstract IAsyncEnumerable<TransformResult> CopySelectionAsync(
        IStepContext context,
        IFileSystemProvider targetProvider,
        string destPath,
        OverwriteMode mode,
        bool skipExistsCheck,
        SourceResult successResult,
        CancellationToken ct);

    public async Task TransferFileAsync(
        IStepContext context,
        DirectoryTreeNode file,
        IFileSystemProvider targetProvider,
        string destination,
        CancellationToken ct)
    {
        // Wire byte-level progress only when the context can consume it (the UI run); benchmarks
        // and tests pass a context that is not a progress sink and skip the allocation.
        IProgress<long>? writeProgress = null;
        if (context is IFileTransferProgressSink progressSink)
        {
            writeProgress = new DelegateProgress<long>(
                bytes => progressSink.ReportFileTransferBytes(file, bytes, file.Size));
        }

        // The provider executes the staging mechanism; the strategy decides the intent (Staged/Direct)
        // per file via SettingsFor and the provider honours it.
        await using var sourceStream = await context.SourceProvider.OpenReadAsync(file.FullPath, ct);
        await targetProvider.WriteAsync(destination, sourceStream, writeProgress, SettingsFor(file.Size), ct);
    }

    /// <summary>
    /// Transfers one file and maps the outcome to a <see cref="TransformResult"/>, marking the node
    /// failed on IO error. Used by the streaming path and the batched large-file bypass.
    /// </summary>
    protected async Task<TransformResult> CopyOneFileAsync(
        IStepContext context,
        DirectoryTreeNode node,
        string destination,
        DestinationResult destResult,
        IFileSystemProvider targetProvider,
        SourceResult successResult,
        CancellationToken ct)
    {
        string? copyError = null;
        try
        {
            await TransferFileAsync(context, node, targetProvider, destination, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            copyError = ex.Message;
        }

        if (copyError is not null)
        {
            context.MarkFailed(node);
            return new TransformResult(IsSuccess: false, SourceNode: node,
                SourceNodeResult: SourceResult.Skipped, ErrorMessage: copyError);
        }

        return new TransformResult(IsSuccess: true, SourceNode: node,
            SourceNodeResult: successResult, DestinationPath: destination,
            DestinationResult: destResult, NumberOfFilesAffected: 1,
            InputBytes: node.Size, OutputBytes: node.Size);
    }

    /// <summary>
    /// Returns the <see cref="DestinationResult"/> for the file, or null if it should be skipped
    /// (OverwriteMode.Skip and destination exists).
    /// </summary>
    protected static async Task<DestinationResult?> ResolveDestResultAsync(
        IFileSystemProvider targetProvider,
        string destination,
        OverwriteMode mode,
        bool skipExistsCheck,
        CancellationToken ct)
    {
        if (skipExistsCheck && mode != OverwriteMode.Skip)
            return DestinationResult.Written;

        var exists = await targetProvider.ExistsAsync(destination, ct);
        if (exists && mode == OverwriteMode.Skip)
            return null;

        return exists ? DestinationResult.Overwritten : DestinationResult.Created;
    }

    protected static TransformResult SkippedResult(DirectoryTreeNode node, string destination) =>
        new(IsSuccess: true, SourceNode: node, SourceNodeResult: SourceResult.Skipped,
            DestinationPath: destination, NumberOfFilesSkipped: 1, InputBytes: node.Size);
}
