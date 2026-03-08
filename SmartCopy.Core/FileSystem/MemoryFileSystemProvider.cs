using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;

namespace SmartCopy.Core.FileSystem;

public sealed class MemoryFileSystemProvider : IFileSystemProvider
{
    private const string DefaultRoot = "/mem";
    private readonly ConcurrentDictionary<string, MemoryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    // SemaphoreSlim(1,1) provides async-compatible exclusive locking for all mutation operations.
    private readonly SemaphoreSlim _mutationSemaphore = new(1, 1);
    
    // Add artificial delay to simulate real I/O for testing progress reporting.
    public bool AddArtificialDelay { get; set; }

    /// <summary>Capacity of the filesystem to report on this filesystem.</summary>
    public long? SimulatedCapacity { get; set; }

    public MemoryFileSystemProvider(
        bool addArtificialDelay = false,
        string? customRootPath = null,
        string? volumeId = null,
        long? capacity = null)
    {
        AddArtificialDelay = addArtificialDelay;
        SimulatedCapacity = capacity;
        RootPath = customRootPath ?? DefaultRoot;
        VolumeId = volumeId ?? "MEM";
        Debug.Assert(RootPath.StartsWith('/'));
        _entries[RootPath] = MemoryEntry.CreateDirectory();
    }

    public string? VolumeId { get; }

    public string RootPath { get; }

    public ProviderCapabilities Capabilities => new(
        CanSeek: true,
        CanAtomicMove: true,
        CanWatch: false,
        MaxPathLength: int.MaxValue,
        CanTrash: false,
        CanQueryFreeSpace: SimulatedCapacity.HasValue);

    public Task<IReadOnlyList<FileSystemNode>> GetChildrenAsync(string path, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            ct.ThrowIfCancellationRequested();
            var normalizedPath = Normalize(path);
            EnsureDirectoryExists(normalizedPath);

            // GetParentPath(kv.Key) == normalizedPath selects direct children.
            // The extra inequality guard is only needed for root, where GetParentPath(RootPath) == RootPath.
            IReadOnlyList<FileSystemNode> children = _entries
                .Where(kv => GetParentPath(kv.Key).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)
                            && !kv.Key.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
                .Select(kv => ToNode(kv.Key, kv.Value))
                .OrderBy(node => node.IsDirectory ? 0 : 1)
                .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (AddArtificialDelay)
            {
                await Task.Delay(10);
            }

            return children;

        }, ct);
    }

    public Task<FileSystemNode> GetNodeAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPath = Normalize(path);

        if (!_entries.TryGetValue(normalizedPath, out var entry))
        {
            throw new FileNotFoundException($"Path does not exist: {normalizedPath}", normalizedPath);
        }

        return Task.FromResult(ToNode(normalizedPath, entry));
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPath = Normalize(path);

        if (!_entries.TryGetValue(normalizedPath, out var entry))
        {
            throw new FileNotFoundException($"Path does not exist: {normalizedPath}", normalizedPath);
        }

        if (entry.IsDirectory)
        {
            throw new InvalidOperationException($"Cannot open directory for read: {normalizedPath}");
        }

        Stream stream = entry.Content != null
            ? new MemoryStream(entry.Content, writable: false)
            : new MemoryStream(Array.Empty<byte>(), writable: false);
            
        return Task.FromResult(stream);
    }

    public async Task WriteAsync(string path, Stream data, IProgress<long>? progress, CancellationToken ct)
    {
        var normalizedPath = Normalize(path);

        // Read stream content before acquiring the mutation lock to avoid holding it during I/O.
        const int chunkSize = 256 * 1024;
        var buffer = new byte[chunkSize];
        await using var output = new MemoryStream();

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await data.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), ct);

            progress?.Report(read);
        }

        var now = DateTime.UtcNow;
        var entry = MemoryEntry.CreateFile(output.ToArray(), now);

        await _mutationSemaphore.WaitAsync(ct);
        try
        {
            EnsureParentDirectoryExists(normalizedPath);
            _entries[normalizedPath] = entry;
            TouchParentModifiedTime(normalizedPath, now);

            if (AddArtificialDelay)
            {
                await Task.Delay(10, ct); // Simulate delay for testing progress reporting
            }
        }
        finally
        {
            _mutationSemaphore.Release();
        }
    }

    public async Task DeleteAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPath = Normalize(path);

        await _mutationSemaphore.WaitAsync(ct);
        try
        {
            if (!_entries.ContainsKey(normalizedPath))
            {
                throw new FileNotFoundException($"Path does not exist: {normalizedPath}", normalizedPath);
            }

            await RemovePathInternal(normalizedPath, ct);
        }
        finally
        {
            _mutationSemaphore.Release();
        }
    }

    public async Task MoveAsync(string sourcePath, string destPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedSourcePath = Normalize(sourcePath);
        var normalizedDestinationPath = Normalize(destPath);

        await _mutationSemaphore.WaitAsync(ct);
        try
        {
            if (!_entries.TryGetValue(normalizedSourcePath, out var sourceEntry))
            {
                throw new FileNotFoundException($"Source path does not exist: {normalizedSourcePath}", normalizedSourcePath);
            }

            if (normalizedSourcePath.Equals(normalizedDestinationPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (sourceEntry.IsDirectory && IsDescendantOf(normalizedDestinationPath, normalizedSourcePath))
            {
                throw new InvalidOperationException("Cannot move a directory into its own descendant.");
            }

            EnsureParentDirectoryExists(normalizedDestinationPath);

            if (_entries.ContainsKey(normalizedDestinationPath))
            {
                await RemovePathInternal(normalizedDestinationPath, ct);
            }

            if (sourceEntry.IsDirectory)
            {
                var keysToMove = _entries.Keys
                    .Where(currentPath => currentPath.Equals(normalizedSourcePath, StringComparison.OrdinalIgnoreCase)
                                          || IsDescendantOf(currentPath, normalizedSourcePath))
                    .OrderBy(pathToMove => pathToMove.Length)
                    .ToList();

                foreach (var oldKey in keysToMove)
                {
                    var relative = oldKey.Equals(normalizedSourcePath, StringComparison.OrdinalIgnoreCase)
                        ? string.Empty
                        : oldKey.Substring(normalizedSourcePath.Length).TrimStart('/');
                    var newKey = string.IsNullOrEmpty(relative)
                        ? normalizedDestinationPath
                        : Normalize(Combine(normalizedDestinationPath, relative));
                    _entries[newKey] = _entries[oldKey];
                }

                foreach (var oldKey in keysToMove)
                {
                    _entries.TryRemove(oldKey, out _);
                }

                return;
            }

            _entries[normalizedDestinationPath] = sourceEntry;
            _entries.TryRemove(normalizedSourcePath, out _);
        }
        finally
        {
            _mutationSemaphore.Release();
        }
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPath = Normalize(path);

        await _mutationSemaphore.WaitAsync(ct);
        try
        {
            EnsureDirectoryExists(normalizedPath, createIfMissing: true);
        }
        finally
        {
            _mutationSemaphore.Release();
        }
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPath = Normalize(path);
        return Task.FromResult(_entries.ContainsKey(normalizedPath));
    }

    public Task<long?> GetAvailableFreeSpaceAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (SimulatedCapacity.HasValue == false)
        {
            throw new ApplicationException("Simulated drive has unlimited capacity");
        }

        long totalBytesUsed = _entries.Values.Sum(x => x.Size);
        long? totalBytesRemaining = SimulatedCapacity.Value - totalBytesUsed;

        if (totalBytesRemaining.Value < 0)
        {
            totalBytesRemaining = 0;    // maybe null?
        }

        return Task.FromResult(totalBytesRemaining);
    }

    private string CombinePath(string basePath, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return Normalize(basePath);
        return Normalize(basePath.TrimEnd('/') + "/" + relativePath.Replace('\\', '/'));
    }

    public string GetRelativePath(string basePath, string fullPath)
    {
        var root = Normalize(basePath);
        var full = Normalize(fullPath);
        if (full.Equals(root, StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        var prefix = root.EndsWith('/') ? root : root + '/';
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? full[prefix.Length..]
            : GetRelativeToRoot(full);
    }

    public string[] SplitPath(string path)
    {
        var normalized = Normalize(path);
        var relative = GetRelativeToRoot(normalized);
        if (string.IsNullOrWhiteSpace(relative))
            return [];
        return relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    public string JoinPath(string basePath, IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
            return Normalize(basePath);
        return CombinePath(basePath, string.Join("/", segments));
    }

    public void SeedDirectory(string path,
        FileAttributes attributes = FileAttributes.Directory)
    {
        var normalizedPath = Normalize(path);
        EnsureDirectoryExists(normalizedPath, createIfMissing: true);
        if (attributes != FileAttributes.Directory && _entries.TryGetValue(normalizedPath, out var e))
            _entries[normalizedPath] = e with { Attributes = attributes };
    }

    public void SeedFile(string path, ReadOnlySpan<byte> content,
        FileAttributes attributes = FileAttributes.Normal)
    {
        var normalizedPath = Normalize(path);
        EnsureParentDirectoryExists(normalizedPath);
        var now = DateTime.UtcNow;
        _entries[normalizedPath] = MemoryEntry.CreateFile(content.ToArray(), now, attributes);
        TouchParentModifiedTime(normalizedPath, now);
    }

    public void SeedSimulatedFile(string path, long size,
        FileAttributes attributes = FileAttributes.Normal)
    {
        var normalizedPath = Normalize(path);
        EnsureParentDirectoryExists(normalizedPath);
        var now = DateTime.UtcNow;
        _entries[normalizedPath] = MemoryEntry.CreateSimulatedFile(size, now, attributes);
        TouchParentModifiedTime(normalizedPath, now);
    }

    private void EnsureParentDirectoryExists(string path)
    {
        var parentPath = GetParentPath(path);
        EnsureDirectoryExists(parentPath, createIfMissing: true);
    }

    private void EnsureDirectoryExists(string path, bool createIfMissing = false)
    {
        if (_entries.TryGetValue(path, out var existingEntry))
        {
            if (!existingEntry.IsDirectory)
            {
                throw new InvalidOperationException($"Path is not a directory: {path}");
            }

            return;
        }

        if (!createIfMissing)
        {
            throw new DirectoryNotFoundException(path);
        }

        if (!path.Equals(RootPath, StringComparison.OrdinalIgnoreCase))
        {
            EnsureDirectoryExists(GetParentPath(path), createIfMissing: true);
        }

        _entries[path] = MemoryEntry.CreateDirectory();
    }

    private async Task RemovePathInternal(string normalizedPath, CancellationToken ct)
    {
        var toRemove = _entries.Keys
            .Where(currentPath => currentPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)
                                  || IsDescendantOf(currentPath, normalizedPath))
            .ToList();

        foreach (var key in toRemove)
        {
            _entries.TryRemove(key, out _);

            if (AddArtificialDelay)
            {
                // Simulate delay for testing progress reporting
                await Task.Delay(10, ct); // Simulate delay for testing progress reporting
            }
        }
    }

    private static bool IsDescendantOf(string path, string ancestorPath)
    {
        if (path.Equals(ancestorPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var prefix = ancestorPath.EndsWith('/') ? ancestorPath : ancestorPath + "/";
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return RootPath;
        }

        var normalized = path.Replace('\\', '/').Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        int iterations = 0;
        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
            Debug.Assert(iterations < 3);
        }

        if (normalized.Length > 1)
        {
            normalized = normalized.TrimEnd('/');
        }

        if (normalized.Equals("/", StringComparison.Ordinal))
        {
            return RootPath;
        }

        if (normalized.Equals(RootPath, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(RootPath + "/", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return RootPath + normalized;
    }

    private string GetParentPath(string path)
    {
        path = Normalize(path);
        if (path.Equals(RootPath, StringComparison.OrdinalIgnoreCase))
        {
            return RootPath;
        }

        var lastSeparator = path.LastIndexOf('/');
        if (lastSeparator <= 0)
        {
            return RootPath;
        }

        return path[..lastSeparator];
    }

    private string Combine(string basePath, string relativePath)
    {
        return Normalize(basePath.TrimEnd('/') + "/" + relativePath.TrimStart('/'));
    }

    private FileSystemNode ToNode(string path, MemoryEntry entry)
    {
        var name = path.Equals(RootPath, StringComparison.OrdinalIgnoreCase)
            ? RootPath
            : path[(path.LastIndexOf('/') + 1)..];

        return new FileSystemNode
        {
            Name = name,
            FullPath = path,
            IsDirectory = entry.IsDirectory,
            Size = entry.IsDirectory ? 0 : entry.Size,
            CreatedAt = entry.CreatedAt,
            ModifiedAt = entry.ModifiedAt,
            Attributes = entry.Attributes,
        };
    }

    private string GetRelativeToRoot(string path)
    {
        if (path.Equals(RootPath, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var root = RootPath.EndsWith('/') ? RootPath : RootPath + '/';
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? path[root.Length..]
            : path.TrimStart('/');
    }

    private string GetCanonicalPath(string path)
    {
        var segments = SplitPath(path);
        return string.Join("/", segments);
    }

    private void TouchParentModifiedTime(string path, DateTime timestamp)
    {
        var parentPath = GetParentPath(path);
        if (_entries.TryGetValue(parentPath, out var parentEntry))
        {
            _entries[parentPath] = parentEntry with { ModifiedAt = timestamp };
        }
    }

    private readonly record struct MemoryEntry(
        bool IsDirectory,
        byte[]? Content,
        long Size,
        DateTime CreatedAt,
        DateTime ModifiedAt,
        FileAttributes Attributes)
    {
        public static MemoryEntry CreateDirectory()
        {
            var now = DateTime.UtcNow;
            return new MemoryEntry(
                IsDirectory: true,
                Content: Array.Empty<byte>(),
                Size: 0,
                CreatedAt: now,
                ModifiedAt: now,
                Attributes: FileAttributes.Directory);
        }

        public static MemoryEntry CreateFile(byte[] content, DateTime timestamp,
            FileAttributes attributes = FileAttributes.Normal)
        {
            return new MemoryEntry(
                IsDirectory: false,
                Content: content,
                Size: content.Length,
                CreatedAt: timestamp,
                ModifiedAt: timestamp,
                Attributes: attributes);
        }

        public static MemoryEntry CreateSimulatedFile(long size, DateTime timestamp,
            FileAttributes attributes = FileAttributes.Normal)
        {
            return new MemoryEntry(
                IsDirectory: false,
                Content: null,
                Size: size,
                CreatedAt: timestamp,
                ModifiedAt: timestamp,
                Attributes: attributes);
        }
    }
}
