namespace SmartCopy.Core.FileSystem;

/// <summary>
/// Provider-owned state for a multi-file write operation.
/// </summary>
public interface IBulkWriteSession : IAsyncDisposable
{
    /// <summary>
    /// Writes <paramref name="data"/> to <paramref name="path"/> within this bulk operation.
    /// </summary>
    Task WriteAsync(
        string path,
        Stream data,
        IProgress<long>? progress,
        OperationalSettings? settings,
        CancellationToken ct);

    /// <summary>
    /// Returns whether a file or directory exists at <paramref name="path"/> within this bulk operation.
    /// </summary>
    Task<bool> ExistsAsync(string path, CancellationToken ct);

    /// <summary>Commits all writes accepted by this session.</summary>
    Task CompleteAsync(CancellationToken ct);
}
