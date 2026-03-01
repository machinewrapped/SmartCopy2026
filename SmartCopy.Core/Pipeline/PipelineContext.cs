using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline;

public sealed class PipelineContext
{
    public required DirectoryTreeNode SourceNode { get; init; }
    public required IFileSystemProvider SourceProvider { get; init; }
    public IFileSystemProvider? TargetProvider { get; set; }

    /// <summary>
    /// The path being built through the pipeline, expressed as separator-free segments.
    /// </summary>
    public required string[] PathSegments { get; set; }

    /// <summary>Canonical forward-slash display form. For logging and preview only.</summary>
    public string DisplayPath => string.Join("/", PathSegments);

    public required string CurrentExtension { get; set; }
    public Stream? ContentStream { get; set; }
    public OverwriteMode OverwriteMode { get; init; } = OverwriteMode.IfNewer;
    public DeleteMode DeleteMode { get; init; } = DeleteMode.Trash;
}
