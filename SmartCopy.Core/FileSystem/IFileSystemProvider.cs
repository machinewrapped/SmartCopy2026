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
    /// Returns the portion of <paramref name="fullPath"/> that is relative to <paramref name="basePath"/>,
    /// using this provider's path conventions. Returns an empty string when the paths are equal.
    /// </summary>
    string GetRelativePath(string basePath, string fullPath);

    /// <summary>
    /// Splits any path string into ordered, separator-free segments.
    /// Both provider-native and canonical forward-slash paths are accepted.
    /// </summary>
    string[] SplitPath(string path);

    /// <summary>
    /// Appends <paramref name="segments"/> onto <paramref name="basePath"/>
    /// using this provider's path conventions.
    /// </summary>
    string JoinPath(string basePath, IReadOnlyList<string> segments);
}

