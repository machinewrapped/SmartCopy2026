namespace SmartCopy.Core.Scanning;

public interface IDirectoryWatcher : IDisposable
{
    event EventHandler? PendingBatchesAvailable;
    event EventHandler<Exception>? WatcherError;

    bool HasPendingBatches { get; }
    IReadOnlyList<DirectoryWatcherBatch> DrainPendingBatches();

    void Start();
    void Stop();
}
