using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline;

public sealed class PipelineContext
{
    public required DirectoryTreeNode SourceNode { get; init; }
    public required IFileSystemProvider SourceProvider { get; init; }
    public required FileSystemProviderRegistry ProviderRegistry { get; init; }

    /// <summary>Resolves the appropriate file system provider for a fully-qualified path.</summary>
    public IFileSystemProvider? ResolveProvider(string path) => ProviderRegistry.Resolve(path);

    /// <summary>The path being built through the pipeline, expressed as separator-free segments.</summary>
    public required string[] PathSegments { get; set; }

    /// <summary>Canonical forward-slash display form. For logging and preview only.</summary>
    public string DisplayPath => string.Join("/", PathSegments);

    public required string CurrentExtension { get; set; }
    public Stream? ContentStream { get; set; }
    public OverwriteMode OverwriteMode { get; init; } = OverwriteMode.IfNewer;
    public DeleteMode DeleteMode { get; init; } = DeleteMode.Trash;

    /// <summary>
    /// Per-run virtual check state used by selection steps' PreviewAsync.
    /// Initialized from the node's real CheckState; only tested and potentially mutated during preview runs.
    /// ApplyAsync reads/writes the real <see cref="DirectoryTreeNode.CheckState"/>.
    /// </summary>
    public CheckState VirtualCheckState { get; set; }
}
