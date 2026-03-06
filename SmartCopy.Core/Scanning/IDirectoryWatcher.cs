namespace SmartCopy.Core.Scanning;

public interface IDirectoryWatcher : IDisposable
{
    event EventHandler<IReadOnlyCollection<string>>? ChangesBatched;
    event EventHandler<Exception>? WatcherError;

    void Start();
    void Stop();
}
