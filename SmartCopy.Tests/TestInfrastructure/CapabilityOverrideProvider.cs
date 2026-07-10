using SmartCopy.Core.FileSystem;
using SmartCopy.Core.FileSystem.Hardware;
namespace SmartCopy.Tests.TestInfrastructure;

/// <summary>
/// Wraps an <see cref="IFileSystemProvider"/> and overrides <see cref="Capabilities"/>
/// to exercise capability-gated code paths in tests without a real cross-volume setup.
/// </summary>
internal sealed class CapabilityOverrideProvider(
    IFileSystemProvider inner,
    ProviderCapabilities capabilities,
    DriveClassification? classification = null,
    string? volumeId = null) : IFileSystemProvider
{
    public ProviderCapabilities Capabilities => capabilities;

    public ValueTask<DriveClassification> GetClassificationAsync(CancellationToken ct = default) =>
        classification is { } c ? ValueTask.FromResult(c) : inner.GetClassificationAsync(ct);
    
    public StringComparer PathComparer => inner.PathComparer;
    
    public StringComparison PathComparison => inner.PathComparison;
    
    public string? VolumeId => volumeId ?? inner.VolumeId;
    
    public string RootPath => inner.RootPath;

    public Task<IReadOnlyList<FileSystemNode>> GetChildrenAsync(string path, CancellationToken ct) =>
        inner.GetChildrenAsync(path, ct);
        
    public Task<FileSystemNode> GetNodeAsync(string path, CancellationToken ct) =>
        inner.GetNodeAsync(path, ct);
        
    public Task<Stream> OpenReadAsync(string path, int? bufferSize = null, CancellationToken ct = default) =>
        inner.OpenReadAsync(path, bufferSize, ct);
    public Task WriteAsync(string path, Stream data, IProgress<long>? progress, OperationalSettings? settings, CancellationToken ct) =>
        inner.WriteAsync(path, data, progress, settings, ct);
    public Task DeleteAsync(string path, CancellationToken ct) =>
        inner.DeleteAsync(path, ct);
        
    public Task MoveAsync(string sourcePath, string destPath, CancellationToken ct) =>
        inner.MoveAsync(sourcePath, destPath, ct);
        
    public Task CreateDirectoryAsync(string path, CancellationToken ct) =>
        inner.CreateDirectoryAsync(path, ct);
        
    public Task<bool> ExistsAsync(string path, CancellationToken ct) =>
        inner.ExistsAsync(path, ct);

    public IBulkWriteSession BeginBulkWrite() =>
        inner.BeginBulkWrite();
        
    public string GetRelativePath(string basePath, string fullPath) =>
        inner.GetRelativePath(basePath, fullPath);
        
    public string[] SplitPath(string path) =>
        inner.SplitPath(path);
        
    public string JoinPath(string basePath, IReadOnlyList<string> segments) =>
        inner.JoinPath(basePath, segments);
        
    public Task<long?> GetAvailableFreeSpaceAsync(CancellationToken ct) =>
        inner.GetAvailableFreeSpaceAsync(ct);
}
