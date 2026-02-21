using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.FileSystem;

public sealed class MemoryFileSystemProvider : IFileSystemProvider
{
    private const string Root = "/";
    private readonly ConcurrentDictionary<string, MemoryEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _mutationLock = new();

    public MemoryFileSystemProvider()
    {
        _entries[Root] = MemoryEntry.CreateDirectory();
    }

    public string RootPath => Root;
    public bool SupportsProgress => true;
    public ProviderCapabilities Capabilities => new(
        CanSeek: true,
        CanAtomicMove: true,
        MaxPathLength: int.MaxValue);

    public Task<IReadOnlyList<FileSystemNode>> GetChildrenAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPath = Normalize(path);
        EnsureDirectoryExists(normalizedPath);

        var children = _entries
            .Where(kv => GetParentPath(kv.Key).Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)
                         && !kv.Key.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase))
            .Select(kv => ToNode(kv.Key, kv.Value, parent: null))
            .OrderBy(node => node.IsDirectory ? 0 : 1)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<FileSystemNode>>(children);
    }

    public Task<FileSystemNode> GetNodeAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPath = Normalize(path);

        if (!_entries.TryGetValue(normalizedPath, out var entry))
        {
            throw new FileNotFoundException($"Path does not exist: {normalizedPath}", normalizedPath);
        }

        return Task.FromResult(ToNode(normalizedPath, entry, parent: null));
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

        Stream stream = new MemoryStream(entry.Content.ToArray(), writable: false);
        return Task.FromResult(stream);
    }

    public async Task WriteAsync(string path, Stream data, IProgress<long>? progress, CancellationToken ct)
    {
        var normalizedPath = Normalize(path);
        EnsureParentDirectoryExists(normalizedPath);

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
        _entries[normalizedPath] = entry;

        TouchParentModifiedTime(normalizedPath, now);
    }

    public Task DeleteAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPath = Normalize(path);

        lock (_mutationLock)
        {
            if (!_entries.ContainsKey(normalizedPath))
            {
                throw new FileNotFoundException($"Path does not exist: {normalizedPath}", normalizedPath);
            }

            RemovePathInternal(normalizedPath);
        }

        return Task.CompletedTask;
    }

    public Task MoveAsync(string sourcePath, string destPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedSourcePath = Normalize(sourcePath);
        var normalizedDestinationPath = Normalize(destPath);

        lock (_mutationLock)
        {
            if (!_entries.TryGetValue(normalizedSourcePath, out var sourceEntry))
            {
                throw new FileNotFoundException($"Source path does not exist: {normalizedSourcePath}", normalizedSourcePath);
            }

            if (normalizedSourcePath.Equals(normalizedDestinationPath, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            if (sourceEntry.IsDirectory && IsDescendantOf(normalizedDestinationPath, normalizedSourcePath))
            {
                throw new InvalidOperationException("Cannot move a directory into its own descendant.");
            }

            EnsureParentDirectoryExists(normalizedDestinationPath);

            if (_entries.ContainsKey(normalizedDestinationPath))
            {
                RemovePathInternal(normalizedDestinationPath);
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

                return Task.CompletedTask;
            }

            _entries[normalizedDestinationPath] = sourceEntry;
            _entries.TryRemove(normalizedSourcePath, out _);
        }

        return Task.CompletedTask;
    }

    public Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPath = Normalize(path);
        EnsureDirectoryExists(normalizedPath, createIfMissing: true);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var normalizedPath = Normalize(path);
        return Task.FromResult(_entries.ContainsKey(normalizedPath));
    }

    public void SeedDirectory(string path)
    {
        var normalizedPath = Normalize(path);
        EnsureDirectoryExists(normalizedPath, createIfMissing: true);
    }

    public void SeedFile(string path, ReadOnlySpan<byte> content)
    {
        var normalizedPath = Normalize(path);
        EnsureParentDirectoryExists(normalizedPath);
        var now = DateTime.UtcNow;
        _entries[normalizedPath] = MemoryEntry.CreateFile(content.ToArray(), now);
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

        if (!path.Equals(Root, StringComparison.OrdinalIgnoreCase))
        {
            EnsureDirectoryExists(GetParentPath(path), createIfMissing: true);
        }

        _entries[path] = MemoryEntry.CreateDirectory();
    }

    private void RemovePathInternal(string normalizedPath)
    {
        var toRemove = _entries.Keys
            .Where(currentPath => currentPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase)
                                  || IsDescendantOf(currentPath, normalizedPath))
            .ToList();

        foreach (var key in toRemove)
        {
            _entries.TryRemove(key, out _);
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

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Root;
        }

        var normalized = path.Replace('\\', '/').Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        if (normalized.Length > 1)
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }

    private static string GetParentPath(string path)
    {
        path = Normalize(path);
        if (path.Equals(Root, StringComparison.OrdinalIgnoreCase))
        {
            return Root;
        }

        var lastSeparator = path.LastIndexOf('/');
        if (lastSeparator <= 0)
        {
            return Root;
        }

        return path[..lastSeparator];
    }

    private static string Combine(string basePath, string relativePath)
    {
        return Normalize(basePath.TrimEnd('/') + "/" + relativePath.TrimStart('/'));
    }

    private FileSystemNode ToNode(string path, MemoryEntry entry, FileSystemNode? parent)
    {
        var name = path.Equals(Root, StringComparison.OrdinalIgnoreCase)
            ? Root
            : path[(path.LastIndexOf('/') + 1)..];

        return new FileSystemNode
        {
            Name = name,
            FullPath = path,
            RelativePath = path.Equals(Root, StringComparison.OrdinalIgnoreCase) ? string.Empty : path.TrimStart('/'),
            IsDirectory = entry.IsDirectory,
            Size = entry.IsDirectory ? 0 : entry.Content.Length,
            CreatedAt = entry.CreatedAt,
            ModifiedAt = entry.ModifiedAt,
            Attributes = entry.Attributes,
            Parent = parent,
        };
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
        byte[] Content,
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
                CreatedAt: now,
                ModifiedAt: now,
                Attributes: FileAttributes.Directory);
        }

        public static MemoryEntry CreateFile(byte[] content, DateTime timestamp)
        {
            return new MemoryEntry(
                IsDirectory: false,
                Content: content,
                CreatedAt: timestamp,
                ModifiedAt: timestamp,
                Attributes: FileAttributes.Normal);
        }
    }
}
