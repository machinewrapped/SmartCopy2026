using System.Runtime.CompilerServices;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline.Strategy;

/// <summary>
/// Encapsulates the byte-transfer mechanics of a copy operation (open-read → write, optional
/// batching, progress, IO-error handling). Selected per source→destination pair by an
/// <see cref="ICopyStrategyPolicy"/> and carrying the resolved <see cref="OperationalSettings"/>.
/// <para>
/// Step-specific orchestration (Copy's flat enumeration vs Move's recursive atomic-subtree walk
/// and source cleanup) stays in the steps; only the transfer is delegated here.
/// </para>
/// </summary>
public interface ICopyStrategy
{
    /// <summary>The settings this strategy was resolved with (buffer, batching, preallocation, ...).</summary>
    OperationalSettings Settings { get; }

    /// <summary>
    /// One-line, human-readable summary of the resolved transfer mechanics — strategy kind, copy
    /// buffer, batching, and write durability — for display in the preview so the user can see what
    /// each step will do without running it. Pure formatting; no I/O.
    /// </summary>
    string Describe();

    /// <summary>
    /// Copies every selected, filter-included file under <see cref="IStepContext.RootNode"/> into
    /// <paramref name="destPath"/>, emitting one <see cref="TransformResult"/> per file. Directories
    /// yield a <see cref="SourceResult.None"/> marker. <paramref name="successResult"/> lets the caller
    /// label outcomes (e.g. <see cref="SourceResult.Copied"/>). Honours <paramref name="mode"/>
    /// for destination-existence handling.
    /// </summary>
    IAsyncEnumerable<TransformResult> CopySelectionAsync(
        IStepContext context,
        IFileSystemProvider targetProvider,
        IBulkWriteSession targetSession,
        string destPath,
        OverwriteMode mode,
        SourceResult successResult,
        CancellationToken ct);

    /// <summary>
    /// Transfers a single file's bytes from the source provider to <paramref name="destination"/>
    /// on <paramref name="targetProvider"/>. Reports progress when the context is an
    /// <see cref="IFileTransferProgressSink"/>. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> on failure (the caller maps it to a result and
    /// performs any cleanup, e.g. source deletion for moves).
    /// </summary>
    Task TransferFileAsync(
        IStepContext context,
        DirectoryTreeNode file,
        IBulkWriteSession targetSession,
        string destination,
        CancellationToken ct);
}
