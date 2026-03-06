namespace SmartCopy.Core.Scanning;

public interface IDirectoryWatcher : IDisposable
{
    event EventHandler? PendingBatchesAvailable;
    event EventHandler<Exception>? WatcherError;
    event EventHandler<string[]>? NotifyNodeWillBeRemoved;

    bool HasPendingBatches { get; }
    IReadOnlyList<DirectoryWatcherBatch> DrainPendingBatches();

    void Start();
    void Stop();
}
