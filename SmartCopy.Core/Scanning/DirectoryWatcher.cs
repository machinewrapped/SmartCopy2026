using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace SmartCopy.Core.Scanning;

public sealed class DirectoryWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounceTimer;
    private readonly object _sync = new();
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);

    public DirectoryWatcher(string path, TimeSpan? debounceWindow = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be empty.", nameof(path));
        }

        DebounceWindow = debounceWindow ?? TimeSpan.FromMilliseconds(300);

        _watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName
                           | NotifyFilters.DirectoryName
                           | NotifyFilters.LastWrite
                           | NotifyFilters.Size
                           | NotifyFilters.Attributes,
        };

        _watcher.Created += OnFileSystemEvent;
        _watcher.Deleted += OnFileSystemEvent;
        _watcher.Changed += OnFileSystemEvent;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnWatcherError;

        _debounceTimer = new Timer(OnDebounceElapsed, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public TimeSpan DebounceWindow { get; }

    public event EventHandler<IReadOnlyCollection<string>>? ChangesBatched;
    public event EventHandler<Exception>? WatcherError;

    public void Start() => _watcher.EnableRaisingEvents = true;

    public void Stop() => _watcher.EnableRaisingEvents = false;

    public void Dispose()
    {
        Stop();
        _watcher.Created -= OnFileSystemEvent;
        _watcher.Deleted -= OnFileSystemEvent;
        _watcher.Changed -= OnFileSystemEvent;
        _watcher.Renamed -= OnRenamed;
        _watcher.Error -= OnWatcherError;
        _watcher.Dispose();
        _debounceTimer.Dispose();
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        QueuePath(e.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        QueuePath(e.OldFullPath);
        QueuePath(e.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        WatcherError?.Invoke(this, e.GetException());
    }

    private void QueuePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (_sync)
        {
            _pendingPaths.Add(path);
            _debounceTimer.Change(DebounceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        IReadOnlyCollection<string> batched;
        lock (_sync)
        {
            if (_pendingPaths.Count == 0)
            {
                return;
            }

            batched = _pendingPaths.ToArray();
            _pendingPaths.Clear();
        }

        ChangesBatched?.Invoke(this, batched);
    }
}

