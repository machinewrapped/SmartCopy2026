using SmartCopy.Core.FileSystem;

namespace SmartCopy.Tests.TestInfrastructure;

/// <summary>
/// Wraps an <see cref="IFileSystemProvider"/> and injects faults (throws <see cref="IOException"/>)
/// on specific operations for error-handling tests.
/// </summary>
internal sealed class FaultingProvider(IFileSystemProvider inner) : IFileSystemProvider
{
    /// <summary>When set, <see cref="OpenReadAsync"/> throws for matching paths.</summary>
    public Func<string, bool>? FaultOnOpen { get; init; }

    /// <summary>When set, <see cref="DeleteAsync"/> throws for matching paths.</summary>
    public Func<string, bool>? FaultOnDelete { get; init; }

    /// <summary>When set, <see cref="MoveAsync"/> throws for matching source paths.</summary>
    public Func<string, bool>? FaultOnMove { get; init; }

    public ProviderCapabilities Capabilities => inner.Capabilities;
    public string? VolumeId => inner.VolumeId;
    public string RootPath => inner.RootPath;

    public Task<IReadOnlyList<FileSystemNode>> GetChildrenAsync(string path, CancellationToken ct) =>
        inner.GetChildrenAsync(path, ct);
    public Task<FileSystemNode> GetNodeAsync(string path, CancellationToken ct) =>
        inner.GetNodeAsync(path, ct);

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct)
    {
        if (FaultOnOpen?.Invoke(path) == true)
            throw new IOException($"Simulated lock on '{path}'.");
        return inner.OpenReadAsync(path, ct);
    }

    public Task WriteAsync(string path, Stream data, IProgress<long>? progress, CancellationToken ct) =>
        inner.WriteAsync(path, data, progress, ct);

    public Task DeleteAsync(string path, CancellationToken ct)
    {
        if (FaultOnDelete?.Invoke(path) == true)
            throw new IOException($"Simulated delete failure on '{path}'.");
        return inner.DeleteAsync(path, ct);
    }

    public Task MoveAsync(string sourcePath, string destPath, CancellationToken ct)
    {
        if (FaultOnMove?.Invoke(sourcePath) == true)
            throw new IOException($"Simulated move failure on '{sourcePath}'.");
        return inner.MoveAsync(sourcePath, destPath, ct);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken ct) =>
        inner.CreateDirectoryAsync(path, ct);
    public Task<bool> ExistsAsync(string path, CancellationToken ct) =>
        inner.ExistsAsync(path, ct);
    public string GetRelativePath(string basePath, string fullPath) =>
        inner.GetRelativePath(basePath, fullPath);
    public string[] SplitPath(string path) =>
        inner.SplitPath(path);
    public string JoinPath(string basePath, IReadOnlyList<string> segments) =>
        inner.JoinPath(basePath, segments);
    public Task<long?> GetAvailableFreeSpaceAsync(CancellationToken ct) =>
        inner.GetAvailableFreeSpaceAsync(ct);
}
