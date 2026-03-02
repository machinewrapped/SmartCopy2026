using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Captures all per-run inputs for a pipeline execution so they can be
/// passed to <see cref="PipelineRunner"/> as a single cohesive object
/// rather than as a long argument list.
/// </summary>
public sealed class PipelineJob
{
    /// <summary>The root of the directory tree to process. Steps traverse it themselves.</summary>
    public required DirectoryTreeNode RootNode { get; init; }

    /// <summary>Provider used to read source files.</summary>
    public required IFileSystemProvider SourceProvider { get; init; }

    /// <summary>Provider used to write/check destination files. May be <see langword="null"/> for delete-only pipelines.</summary>
    public IFileSystemProvider? TargetProvider { get; init; }

    /// <summary>Controls how pre-existing destination files are handled.</summary>
    public OverwriteMode OverwriteMode { get; init; } = OverwriteMode.IfNewer;

    /// <summary>Controls whether deleted files go to the recycle bin or are permanently removed.</summary>
    public DeleteMode DeleteMode { get; init; } = DeleteMode.Trash;

    /// <summary>Provider registry for resolving paths during pipeline execution.</summary>
    public FileSystemProviderRegistry? ProviderRegistry { get; init; }
}
