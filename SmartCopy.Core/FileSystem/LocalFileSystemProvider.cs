using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.FileSystem;

public sealed class LocalFileSystemProvider : IFileSystemProvider
{
    public LocalFileSystemProvider(string rootPath)
    {
        RootPath = PathHelper.Normalize(rootPath);
    }

    public string RootPath { get; }
    public bool SupportsProgress => true;
    public ProviderCapabilities Capabilities => new(
        CanSeek: true,
        CanAtomicMove: true,
        MaxPathLength: int.MaxValue);

    public Task<IReadOnlyList<FileSystemNode>> GetChildrenAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fullPath = Resolve(path);

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException(fullPath);
        }

        var nodes = new List<FileSystemNode>();

        foreach (var childDirectory in Directory.EnumerateDirectories(fullPath))
        {
            ct.ThrowIfCancellationRequested();
            nodes.Add(CreateDirectoryNode(childDirectory, parent: null));
        }

        foreach (var childFile in Directory.EnumerateFiles(fullPath))
        {
            ct.ThrowIfCancellationRequested();
            nodes.Add(CreateFileNode(childFile, parent: null));
        }

        return Task.FromResult<IReadOnlyList<FileSystemNode>>(nodes);
    }

    public Task<FileSystemNode> GetNodeAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fullPath = Resolve(path);

        if (Directory.Exists(fullPath))
        {
            return Task.FromResult(CreateDirectoryNode(fullPath, parent: null));
        }

        if (File.Exists(fullPath))
        {
            return Task.FromResult(CreateFileNode(fullPath, parent: null));
        }

        throw new FileNotFoundException($"Path does not exist: {fullPath}", fullPath);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fullPath = Resolve(path);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"File does not exist: {fullPath}", fullPath);
        }

        Stream stream = File.Open(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Task.FromResult(stream);
    }

    public async Task WriteAsync(string path, Stream data, IProgress<long>? progress, CancellationToken ct)
    {
        var fullPath = Resolve(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        const int chunkSize = 256 * 1024;
        await using var output = File.Open(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[chunkSize];

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
    }

    public Task DeleteAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fullPath = Resolve(path);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            return Task.CompletedTask;
        }

        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
            return Task.CompletedTask;
        }

        throw new FileNotFoundException($"Path does not exist: {fullPath}", fullPath);
    }

    public Task MoveAsync(string sourcePath, string destPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fullSourcePath = Resolve(sourcePath);
        var fullDestinationPath = Resolve(destPath);

        var destinationParent = Path.GetDirectoryName(fullDestinationPath);
        if (!string.IsNullOrEmpty(destinationParent))
        {
            Directory.CreateDirectory(destinationParent);
        }

        if (File.Exists(fullSourcePath))
        {
            if (File.Exists(fullDestinationPath))
            {
                File.Delete(fullDestinationPath);
            }

            File.Move(fullSourcePath, fullDestinationPath);
            return Task.CompletedTask;
        }

        if (Directory.Exists(fullSourcePath))
        {
            if (Directory.Exists(fullDestinationPath))
            {
                Directory.Delete(fullDestinationPath, recursive: true);
            }

            Directory.Move(fullSourcePath, fullDestinationPath);
            return Task.CompletedTask;
        }

        throw new FileNotFoundException($"Source path does not exist: {fullSourcePath}", fullSourcePath);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fullPath = Resolve(path);
        Directory.CreateDirectory(fullPath);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var fullPath = Resolve(path);
        return Task.FromResult(File.Exists(fullPath) || Directory.Exists(fullPath));
    }

    private string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return RootPath;
        }

        if (Path.IsPathFullyQualified(path))
        {
            return PathHelper.Normalize(path);
        }

        return PathHelper.CombineForProvider(RootPath, path);
    }

    private FileSystemNode CreateDirectoryNode(string directoryPath, FileSystemNode? parent)
    {
        var info = new DirectoryInfo(directoryPath);
        return new FileSystemNode
        {
            Name = info.Name,
            FullPath = info.FullName,
            RelativePath = PathHelper.GetRelativePath(RootPath, info.FullName),
            IsDirectory = true,
            Size = 0,
            CreatedAt = info.CreationTimeUtc,
            ModifiedAt = info.LastWriteTimeUtc,
            Attributes = info.Attributes,
            Parent = parent,
        };
    }

    private FileSystemNode CreateFileNode(string filePath, FileSystemNode? parent)
    {
        var info = new FileInfo(filePath);
        return new FileSystemNode
        {
            Name = info.Name,
            FullPath = info.FullName,
            RelativePath = PathHelper.GetRelativePath(RootPath, info.FullName),
            IsDirectory = false,
            Size = info.Length,
            CreatedAt = info.CreationTimeUtc,
            ModifiedAt = info.LastWriteTimeUtc,
            Attributes = info.Attributes,
            Parent = parent,
        };
    }
}

