namespace SmartCopy.Core.FileSystem;

public readonly record struct ProviderCapabilities(
    bool CanSeek,
    bool CanAtomicMove,
    bool CanWatch,
    int MaxPathLength,
    bool CanTrash = false,
    bool CanQueryFreeSpace = false,
    bool AllowBatchRead = true,
    bool AllowBatchWrite = true,
    long MaxBatchBufferBytes = 0,
    bool AllowStagedWrite = true)
{
    /// <summary>Full capabilities; used as a safe default before a source path is configured.</summary>
    public static ProviderCapabilities Full { get; } =
        new(CanSeek: true, CanAtomicMove: true, CanWatch: true, MaxPathLength: int.MaxValue, CanTrash: true, CanQueryFreeSpace: true);
}
