using System.IO;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline;

public sealed class TransformContext
{
    public required FileSystemNode SourceNode { get; init; }
    public required IFileSystemProvider SourceProvider { get; init; }
    public IFileSystemProvider? TargetProvider { get; set; }
    public required string CurrentPath { get; set; }
    public required string CurrentExtension { get; set; }
    public Stream? ContentStream { get; set; }
    public OverwriteMode OverwriteMode { get; init; } = OverwriteMode.IfNewer;
    public DeleteMode DeleteMode { get; init; } = DeleteMode.Trash;
}

