namespace SmartCopy.Core.FileSystem;

internal sealed class ProviderBulkWriteSession(IFileSystemProvider provider) : IBulkWriteSession
{
    public Task WriteAsync(
        string path,
        Stream data,
        IProgress<long>? progress,
        OperationalSettings? settings,
        CancellationToken ct) =>
        provider.WriteAsync(path, data, progress, settings, ct);

    public Task<bool> ExistsAsync(string path, CancellationToken ct) =>
        provider.ExistsAsync(path, ct);

    public Task CompleteAsync(CancellationToken ct) => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
