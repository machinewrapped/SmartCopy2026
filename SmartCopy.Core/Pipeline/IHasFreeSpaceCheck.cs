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
        $"Not enough space on {TargetRootPath} — {FileSizeFormatter.FormatBytes(NeededBytes)} needed, " +
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
    /// Checks free space using a pre-cached map (keyed by provider RootPath).
    /// Returns null when no check is applicable (same-volume move, no destination, unknown free space).
    /// Returns a result in all other cases — examine <see cref="FreeSpaceValidationResult.IsViolation"/>
    /// to determine whether space is insufficient. The result always carries
    /// <see cref="FreeSpaceValidationResult.NeededBytes"/> so callers can deduct consumption
    /// from the cache to correctly account for cumulative multi-step usage.
    /// </summary>
    FreeSpaceValidationResult? ValidateFreeSpace(
        long bytesNeeded,
        IFileSystemProvider source,
        IPathResolver registry,
        IReadOnlyDictionary<string, long?> freeSpaceCache);
}
