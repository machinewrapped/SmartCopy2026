using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Scanning;

public sealed class DirectoryWatcher : IDirectoryWatcher
{
    private readonly IFileSystemProvider _provider;
    private readonly string _rootPath;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _debounceTimer;
    private readonly System.Threading.Lock _sync = new();
    private readonly HashSet<string> _pendingDeletedPaths = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingPathsToAdd = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingPathsToRefresh = new(StringComparer.Ordinal);
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
    public event EventHandler<string[]>? NotifyNodeWillBeRemoved;
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
                // Scan the path to build a subtree
                QueueScanPath(e.FullPath);
                break;
            case WatcherChangeTypes.Changed:
                // Just refresh filesystem metadata
                QueueRefreshPath(e.FullPath);
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

        var relativeSegments = TryNormalizePath(path);
        if (relativeSegments is not null)
        {
            NotifyNodeWillBeRemoved?.Invoke(this, relativeSegments);
        }

        lock (_sync)
        {
            _pendingDeletedPaths.Add(path);
            _pendingPathsToAdd.Remove(path);
            _pendingPathsToRefresh.Remove(path);
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
            _pendingPathsToAdd.Add(path);
            _pendingPathsToRefresh.Remove(path);
            _debounceTimer.Change(DebounceWindow, Timeout.InfiniteTimeSpan);
        }
    }

    private void QueueRefreshPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (_sync)
        {
            if (!_pendingDeletedPaths.Contains(path) && !_pendingPathsToAdd.Contains(path))
            {
                _pendingPathsToRefresh.Add(path);
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
                string[] pathsToAdd;
                string[] pathsToRefresh;
                lock (_sync)
                {
                    if (_pendingDeletedPaths.Count == 0 && _pendingPathsToAdd.Count == 0 && _pendingPathsToRefresh.Count == 0)
                    {
                        break;
                    }

                    deletedPaths = [.. _pendingDeletedPaths];
                    pathsToAdd = [.. _pendingPathsToAdd];
                    pathsToRefresh = [.. _pendingPathsToRefresh];
                    
                    _pendingDeletedPaths.Clear();
                    _pendingPathsToAdd.Clear();
                    _pendingPathsToRefresh.Clear();
                }

                DirectoryWatcherBatch batch;
                try
                {
                    batch = await BuildBatchAsync(deletedPaths, pathsToAdd, pathsToRefresh);
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
        IReadOnlyList<string> pathsToScan,
        IReadOnlyList<string> pathsToRefresh)
    {
        var deletions = deletedPaths
            .Select(TryNormalizePath)
            .Where(segments => segments is not null)
            .Select(segments => new DirectoryWatcherDeletion(segments!))
            .GroupBy(d => d.CanonicalRelativePath, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var inserts = new List<DirectoryWatcherInsert>();

        foreach (var relativeSegments in NormalizeScanTargets(pathsToScan))
        {
            var fullPath = _provider.JoinPath(_rootPath, relativeSegments);
            if (!await _provider.ExistsAsync(fullPath, CancellationToken.None))
            {
                continue;
            }

            ScannedNode snapshot;
            try
            {
                snapshot = await _scanner.BuildScannedSubtreeAsync(
                    fullPath,
                    new ScanOptions { LazyExpand = false, IncludeHidden = true },
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                WatcherError?.Invoke(this, ex);
                continue;
            }

            inserts.Add(new DirectoryWatcherInsert(relativeSegments, snapshot));
        }

        var refreshes = new List<DirectoryWatcherRefresh>();
        foreach (var path in pathsToRefresh)
        {
            var relativeSegments = TryNormalizePath(path);
            if (relativeSegments is null)
            {
                continue;
            }

            var fullPath = _provider.JoinPath(_rootPath, relativeSegments);
            var node = await _provider.GetNodeAsync(fullPath, CancellationToken.None);
            if (node is null)
            {
                continue;
            }

            refreshes.Add(new DirectoryWatcherRefresh(relativeSegments, node));
        }

        return new DirectoryWatcherBatch(
            requiresFullRescan: false,
            deletions: [..deletions],
            inserts: [..inserts],
            refreshes: [..refreshes]);
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

        if (normalized.Count == 0)
        {
            return [];
        }

        var sortedPaths = normalized.Values.OrderBy(s => string.Join("/", s), StringComparer.Ordinal).ToList();
        
        var minimalRoots = new List<string[]>();
        var lastAdded = sortedPaths[0];
        minimalRoots.Add(lastAdded);

        foreach (var path in sortedPaths.Skip(1))
        {
            if (!IsSameOrAncestor(lastAdded, path))
            {
                minimalRoots.Add(path);
                lastAdded = path;
            }
        }

        return minimalRoots;
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

    private static bool IsSameOrAncestor(string[] ancestorSegments, string[] candidateSegments)
    {
        if (ancestorSegments.Length > candidateSegments.Length)
        {
            return false;
        }

        for (int i = 0; i < ancestorSegments.Length; i++)
        {
            if (!string.Equals(ancestorSegments[i], candidateSegments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
