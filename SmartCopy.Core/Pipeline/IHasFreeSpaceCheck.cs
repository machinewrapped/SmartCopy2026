using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Structured result of a free-space check, carrying raw byte counts so callers
/// can format messages appropriate to their context (step card vs preview pane).
/// Also carries <see cref="TargetRootPath"/> so callers can update a running
/// free-space cache to account for cumulative consumption across multiple steps.
/// </summary>
public sealed record FreeSpaceValidationResult(long NeededBytes, long FreeBytes, string TargetPath)
{
    /// <summary>True when <see cref="NeededBytes"/> exceeds <see cref="FreeBytes"/>.</summary>
    public bool IsViolation => NeededBytes > FreeBytes;

    public long OverBytes => NeededBytes - FreeBytes;

    /// <summary>Compact message for step cards.</summary>
    public string ShortMessage =>
        $"Not enough space — {FileSizeFormatter.FormatBytes(OverBytes)} over";

    /// <summary>Full message for the preview warnings pane.</summary>
    public string LongMessage =>
        $"Not enough space in {TargetPath} — {FileSizeFormatter.FormatBytes(NeededBytes)} needed, " +
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
    /// Checks free space using a cache keyed by provider RootPath.
    /// Returns null when inapplicable or no check is possible (same-volume move, no destination, unknown free space).
    /// Otherwise, returns a <see cref="FreeSpaceValidationResult"/> with IsViolation set if free space is insufficient.
    /// The free space cache is updated to subtract the space consumed by this operation.
    /// </summary>
    Task<FreeSpaceValidationResult?> ValidateFreeSpace(
        long bytesNeeded,
        IFileSystemProvider source,
        IPathResolver registry,
        Dictionary<string, long?> freeSpaceCache,
        CancellationToken ct);
}
