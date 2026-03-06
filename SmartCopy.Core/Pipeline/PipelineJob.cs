using System;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Progress;

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

    /// <summary>Controls how pre-existing destination files are handled.</summary>
    public OverwriteMode OverwriteMode { get; init; } = OverwriteMode.IfNewer;

    /// <summary>Controls whether deleted files go to the recycle bin or are permanently removed.</summary>
    public DeleteMode DeleteMode { get; init; } = DeleteMode.Trash;

    /// <summary>Progress reporter for overall pipeline execution.</summary>
    public IProgress<OperationProgress>? Progress { get; init; }

    /// <summary>Progress reporter for individual node transformations.</summary>
    public IProgress<TransformResult>? NodeProgress { get; init; }

    /// <summary>Token for pausing/resuming the pipeline.</summary>
    public PauseTokenSource? PauseToken { get; init; }

    /// <summary>Token for cancelling the pipeline.</summary>
    public CancellationToken CancellationToken { get; init; }
}
