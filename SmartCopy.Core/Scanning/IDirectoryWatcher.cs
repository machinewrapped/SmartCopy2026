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

    /// <summary>
    /// Builds changes still waiting out the debounce window into batches without waiting for it,
    /// after any build already in progress finishes. Call after <see cref="Stop"/> to collect
    /// everything the watcher saw before it is disposed.
    /// </summary>
    Task FlushAsync();
}
