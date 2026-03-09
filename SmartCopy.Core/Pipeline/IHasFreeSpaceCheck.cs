using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Structured result of a free-space check, carrying raw byte counts so callers
/// can format messages appropriate to their context (step card vs preview pane).
/// Also carries <see cref="TargetRootPath"/> so callers can update a running
/// free-space cache to account for cumulative consumption across multiple steps.
/// </summary>
public sealed record FreeSpaceValidationResult(long NeededBytes, long FreeBytes, string TargetRootPath)
{
    /// <summary>True when <see cref="NeededBytes"/> exceeds <see cref="FreeBytes"/>.</summary>
    public bool IsViolation => NeededBytes > FreeBytes;

    public long OverBytes => NeededBytes - FreeBytes;

    /// <summary>Compact message for step cards.</summary>
    public string ShortMessage =>
        $"Not enough space — {FileSizeFormatter.FormatBytes(OverBytes)} over";

    /// <summary>Full message for the preview warnings pane.</summary>
    public string LongMessage =>
        $"Not enough space in {TargetRootPath} — {FileSizeFormatter.FormatBytes(NeededBytes)} needed, " +
        $"{FileSizeFormatter.FormatBytes(FreeBytes)} free " +
        $"({FileSizeFormatter.FormatBytes(OverBytes)} over)";

    /// <summary>Null result for when no check is possible or needed.</summary>
    public static Task<FreeSpaceValidationResult?> NullResult 
        => Task.FromResult<FreeSpaceValidationResult?>(null);

    /// <summary>Result for when free space check is completed.</summary>
    public static Task<FreeSpaceValidationResult?> Result(long neededBytes, long freeBytes, string targetRootPath)
        => Task.FromResult<FreeSpaceValidationResult?>(new FreeSpaceValidationResult(neededBytes, freeBytes, targetRootPath));
}

/// <summary>
/// Implemented by pipeline steps that write data to a target volume, allowing pre-flight free-space validation.
/// </summary>
public interface IHasFreeSpaceCheck
{
    /// <summary>
    /// Checks free space for the destination volume using a cumulative cache.
    /// Returns null when inapplicable or no check is possible (same-volume move, no destination, unknown free space).
    /// Otherwise, returns a <see cref="FreeSpaceValidationResult"/> with IsViolation set if free space is insufficient.
    /// </summary>
    Task<FreeSpaceValidationResult?> ValidateFreeSpace(
        long bytesNeeded,
        IFileSystemProvider source,
        IPathResolver registry,
        FreeSpaceCache freeSpaceCache,
        CancellationToken ct);
}
