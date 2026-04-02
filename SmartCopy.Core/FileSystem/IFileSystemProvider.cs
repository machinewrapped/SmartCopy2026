namespace SmartCopy.Core.FileSystem;

public interface IFileSystemProvider
{
    /// <summary>
    /// Identifies the storage volume this provider operates on.
    /// Values are provider-specific and may be <see langword="null"/> when unknown.
    /// </summary>
    string? VolumeId { get; }

    /// <summary>
    /// Gets the root path this provider is scoped to.
    /// </summary>
    string RootPath { get; }

    /// <summary>
    /// Describes optional capabilities supported by this provider.
    /// </summary>
    ProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Gets the immediate child nodes for a directory path.
    /// </summary>
    Task<IReadOnlyList<FileSystemNode>> GetChildrenAsync(string path, CancellationToken ct);

    /// <summary>
    /// Gets metadata for a single file or directory path.
    /// </summary>
    Task<FileSystemNode> GetNodeAsync(string path, CancellationToken ct);

    /// <summary>
    /// Opens a readable stream for the file at <paramref name="path"/>.
    /// </summary>
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);

    /// <summary>
    /// Writes <paramref name="data"/> to <paramref name="path"/>.
    /// Implementations should commit the destination only after the full payload is written
    /// (for example via temp-file staging + rename) to avoid leaving truncated files when
    /// interrupted. When true transactional commit is unavailable, implementations should
    /// still best-effort clean up partial output before returning an error.
    /// </summary>
    Task WriteAsync(string path, Stream data, IProgress<long>? progress, CancellationToken ct);

    /// <summary>
    /// Deletes a file or directory at <paramref name="path"/>.
    /// </summary>
    Task DeleteAsync(string path, CancellationToken ct);

    /// <summary>
    /// Moves a file or directory from <paramref name="sourcePath"/> to <paramref name="destPath"/>.
    /// </summary>
    Task MoveAsync(string sourcePath, string destPath, CancellationToken ct);

    /// <summary>
    /// Creates a directory at <paramref name="path"/> and any missing parent directories.
    /// </summary>
    Task CreateDirectoryAsync(string path, CancellationToken ct);

    /// <summary>
    /// Returns whether a file or directory exists at <paramref name="path"/>.
    /// </summary>
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

    /// <summary>
    /// Returns the last non-empty segment of <paramref name="path"/> (i.e. the filename),
    /// using this provider's path conventions.
    /// </summary>
    string GetFileName(string path)
    {
        var segments = SplitPath(path);
        return segments.LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? string.Empty;
    }

    /// <summary>
    /// Returns the available free bytes on the volume hosting this provider,
    /// or <see langword="null"/> if the provider does not support querying free space
    /// (see <see cref="ProviderCapabilities.CanQueryFreeSpace"/>).
    /// </summary>
    Task<long?> GetAvailableFreeSpaceAsync(CancellationToken ct);
}

