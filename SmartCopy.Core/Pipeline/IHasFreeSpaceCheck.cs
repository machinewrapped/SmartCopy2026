using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Structured result of a free-space check, carrying raw byte counts so callers
/// can format messages appropriate to their context (step card vs preview pane).
/// </summary>
public sealed record FreeSpaceValidationResult(long NeededBytes, long FreeBytes)
{
    public long OverBytes => NeededBytes - FreeBytes;

    /// <summary>Compact message for step cards.</summary>
    public string ShortMessage =>
        $"Not enough space — {FileSizeFormatter.FormatBytes(OverBytes)} over";

    /// <summary>Full message for the preview warnings pane.</summary>
    public string LongMessage =>
        $"Not enough space — {FileSizeFormatter.FormatBytes(NeededBytes)} needed, " +
        $"{FileSizeFormatter.FormatBytes(FreeBytes)} free " +
        $"({FileSizeFormatter.FormatBytes(OverBytes)} over)";
}

/// <summary>
/// Implemented by pipeline steps that write data to a target volume,
/// allowing <see cref="PipelineRunner"/> to perform a pre-flight free-space check.
/// </summary>
public interface IHasFreeSpaceCheck
{
    /// <summary>
    /// Returns the provider that will receive output from this step,
    /// or <see langword="null"/> if no free-space check is needed
    /// (e.g., same-volume moves that require no additional space).
    /// </summary>
    IFileSystemProvider? ResolveFreeSpaceTarget(IFileSystemProvider sourceProvider, IPathResolver registry);

    /// <summary>
    /// Synchronous check against a pre-cached free-space map (keyed by provider RootPath).
    /// Returns a <see cref="FreeSpaceValidationResult"/> if space is insufficient, null otherwise.
    /// Used by PipelineValidator for real-time step-card warnings.
    /// </summary>
    FreeSpaceValidationResult? ValidateFreeSpace(
        long bytesNeeded,
        IFileSystemProvider source,
        IPathResolver registry,
        IReadOnlyDictionary<string, long?> freeSpaceCache);
}
