namespace SmartCopy.Core.FileSystem;

public sealed class FileSystemNode
{
    // Filesystem data (immutable after scan)
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;

    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ModifiedAt { get; init; }
    public FileAttributes Attributes { get; init; }
}
