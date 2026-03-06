using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Scanning;

public sealed class DirectoryWatcher : IDirectoryWatcher
{
    private readonly IFileSystemProvider _provider;
    private readonly string _rootPath;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounceTimer;
    private readonly object _sync = new();
    private readonly HashSet<string> _pendingDeletedPaths = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingPathsToScan = new(StringComparer.Ordinal);
    private readonly Queue<DirectoryWatcherBatch> _pendingBatches = new();
    private readonly SemaphoreSlim _buildGate = new(1, 1);
    private readonly DirectoryScanner _scanner;

    public DirectoryWatcher(IFileSystemProvider provider, string path, TimeSpan? debounceWindow = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path must not be empty.", nameof(path));
        }

        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _rootPath = path;
        _scanner = new DirectoryScanner(provider);
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

    public event EventHandler? PendingBatchesAvailable;
    public event EventHandler<Exception>? WatcherError;
    public bool HasPendingBatches
    {
        get
        {
            lock (_sync)
            {
                return _pendingBatches.Count > 0;
            }
        }
    }

    public void Start() => _watcher.EnableRaisingEvents = true;

    public void Stop() => _watcher.EnableRaisingEvents = false;

    public IReadOnlyList<DirectoryWatcherBatch> DrainPendingBatches()
    {
        lock (_sync)
        {
            if (_pendingBatches.Count == 0)
            {
                return [];
            }

            var batches = _pendingBatches.ToArray();
            _pendingBatches.Clear();
            return batches;
        }
    }

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
        _buildGate.Dispose();
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        switch (e.ChangeType)
        {
            case WatcherChangeTypes.Created:
            case WatcherChangeTypes.Changed:
                QueueScanPath(e.FullPath);
                break;
            case WatcherChangeTypes.Deleted:
                QueueDeletedPath(e.FullPath);
                break;
        }
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        QueueDeletedPath(e.OldFullPath);
        QueueScanPath(e.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        WatcherError?.Invoke(this, e.GetException());
    }

    private void QueueDeletedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (_sync)
        {
            _pendingDeletedPaths.Add(path);
            _pendingPathsToScan.Remove(path);
            _debounceTimer.Change(DebounceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void QueueScanPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (_sync)
        {
            if (!_pendingDeletedPaths.Contains(path))
            {
                _pendingPathsToScan.Add(path);
            }

            _debounceTimer.Change(DebounceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void OnDebounceElapsed(object? state)
    {
        _ = BuildAndEmitPendingBatchesAsync();
    }

    private async Task BuildAndEmitPendingBatchesAsync()
    {
        if (!await _buildGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            while (true)
            {
                string[] deletedPaths;
                string[] pathsToScan;
                lock (_sync)
                {
                    if (_pendingDeletedPaths.Count == 0 && _pendingPathsToScan.Count == 0)
                    {
                        break;
                    }

                    deletedPaths = [.. _pendingDeletedPaths];
                    pathsToScan = [.. _pendingPathsToScan];
                    _pendingDeletedPaths.Clear();
                    _pendingPathsToScan.Clear();
                }

                DirectoryWatcherBatch batch;
                try
                {
                    batch = await BuildBatchAsync(deletedPaths, pathsToScan);
                }
                catch (Exception ex)
                {
                    WatcherError?.Invoke(this, ex);
                    continue;
                }

                if (!batch.IsEmpty)
                {
                    lock (_sync)
                    {
                        _pendingBatches.Enqueue(batch);
                    }

                    PendingBatchesAvailable?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        finally
        {
            _buildGate.Release();
        }
    }

    private async Task<DirectoryWatcherBatch> BuildBatchAsync(
        IReadOnlyList<string> deletedPaths,
        IReadOnlyList<string> pathsToScan)
    {
        var deletions = deletedPaths
            .Select(TryNormalizePath)
            .Where(segments => segments is not null)
            .Select(segments => new DirectoryWatcherDeletion(segments!))
            .GroupBy(d => d.CanonicalRelativePath, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var upserts = new List<DirectoryWatcherUpsert>();
        foreach (var relativeSegments in NormalizeScanTargets(pathsToScan))
        {
            var fullPath = _provider.JoinPath(_rootPath, relativeSegments);
            if (!await _provider.ExistsAsync(fullPath, CancellationToken.None))
            {
                continue;
            }

            DirectoryTreeNode snapshot;
            try
            {
                snapshot = await _scanner.BuildSubtreeAsync(
                    fullPath,
                    parent: null,
                    initialCheckState: CheckState.Unchecked,
                    new ScanOptions { LazyExpand = false, IncludeHidden = true },
                    CancellationToken.None);
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                continue;
            }

            upserts.Add(new DirectoryWatcherUpsert(relativeSegments, snapshot));
        }

        return new DirectoryWatcherBatch(
            requiresFullRescan: false,
            deletions: deletions,
            upserts: upserts);
    }

    private IEnumerable<string[]> NormalizeScanTargets(IEnumerable<string> paths)
    {
        var normalized = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            var relativeSegments = TryNormalizePath(path);
            if (relativeSegments is null)
            {
                continue;
            }

            normalized.TryAdd(string.Join("/", relativeSegments), relativeSegments);
        }

        return normalized.Values
            .OrderBy(segments => segments.Length)
            .Where(candidate => !normalized.Values.Any(existing =>
                !ReferenceEquals(existing, candidate) && IsSameOrAncestor(existing, candidate)));
    }

    private string[]? TryNormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var relativePath = _provider.GetRelativePath(_rootPath, path);
        var relativeSegments = _provider.SplitPath(relativePath);
        if (relativeSegments.Length > 0 && string.Equals(relativeSegments[0], "..", StringComparison.Ordinal))
        {
            return null;
        }

        return relativeSegments;
    }

    private static bool IsSameOrAncestor(IReadOnlyList<string> ancestorSegments, IReadOnlyList<string> candidateSegments)
    {
        if (ancestorSegments.Count > candidateSegments.Count)
        {
            return false;
        }

        for (int i = 0; i < ancestorSegments.Count; i++)
        {
            if (!string.Equals(ancestorSegments[i], candidateSegments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
