using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.FileSystem;

public interface IFileSystemProvider
{
    string RootPath { get; }
    bool SupportsProgress { get; }
    ProviderCapabilities Capabilities { get; }

    Task<IReadOnlyList<FileSystemNode>> GetChildrenAsync(string path, CancellationToken ct);
    Task<FileSystemNode> GetNodeAsync(string path, CancellationToken ct);
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);
    Task WriteAsync(string path, Stream data, IProgress<long>? progress, CancellationToken ct);
    Task DeleteAsync(string path, CancellationToken ct);
    Task MoveAsync(string sourcePath, string destPath, CancellationToken ct);
    Task CreateDirectoryAsync(string path, CancellationToken ct);
    Task<bool> ExistsAsync(string path, CancellationToken ct);

    /// <summary>
    /// Combines a base path with a relative path fragment using this provider's path conventions.
    /// </summary>
    string CombinePath(string basePath, string relativePath);

    /// <summary>
    /// Returns the portion of <paramref name="fullPath"/> that is relative to <paramref name="basePath"/>,
    /// using this provider's path conventions. Returns an empty string when the paths are equal.
    /// </summary>
    string GetRelativePath(string basePath, string fullPath);
}

