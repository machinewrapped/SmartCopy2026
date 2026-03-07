using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Provides step implementations with access to the tree root, providers, and per-node
/// transform contexts. Created once per pipeline run; contexts are cached so PathSegments
/// mutations from earlier steps are visible to later ones.
/// </summary>
public interface IStepContext
{
    DirectoryTreeNode RootNode { get; }
    IFileSystemProvider SourceProvider { get; }
    FileSystemProviderRegistry ProviderRegistry { get; }

    bool ShowHiddenFiles { get; }
    bool AllowDeleteReadOnly { get; }

    /// <summary>Returns the cached (or newly created) <see cref="PipelineContext"/> for a node.
    /// PathSegments mutations persist across all steps in the run.</summary>
    PipelineContext GetNodeContext(DirectoryTreeNode node);

    bool IsNodeFailed(DirectoryTreeNode node);
    void MarkFailed(DirectoryTreeNode node);
}
