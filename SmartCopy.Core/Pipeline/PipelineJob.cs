using System;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Progress;
using SmartCopy.Core.Trash;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Captures all per-run inputs for a pipeline execution so they can be
/// passed to <see cref="PipelineRunner"/> as a single cohesive object
/// rather than as a long argument list.
/// </summary>
public sealed record PipelineJob
{
    /// <summary>The root of the directory tree to process. Steps traverse it themselves.</summary>
    public required DirectoryTreeNode RootNode { get; init; }

    /// <summary>Provider used to read source files.</summary>
    public required IFileSystemProvider SourceProvider { get; init; }

    /// <summary>Provider registry for resolving paths during pipeline execution.</summary>
    public required FileSystemProviderRegistry ProviderRegistry { get; init; }

    /// <summary>Service used to move files/directories to the OS-level trash.</summary>
    public ITrashService TrashService { get; init; } = new NullTrashService();

    /// <summary>Whether hidden files should be visible to pipeline steps.</summary>
    public bool ShowHiddenFiles { get; init; }

    /// <summary>Whether read-only files can be deleted by pipeline steps.</summary>
    public bool AllowDeleteReadOnly { get; init; }

    /// <summary>Progress reporter for overall pipeline execution.</summary>
    public IProgress<OperationProgress>? Progress { get; init; }

    /// <summary>Progress reporter for individual node transformations.</summary>
    public IProgress<TransformResult>? NodeProgress { get; init; }

    /// <summary>Token for pausing/resuming the pipeline.</summary>
    public PauseTokenSource? PauseToken { get; init; }

    /// <summary>Token for cancelling the pipeline.</summary>
    public CancellationToken CancellationToken { get; init; }
}
