using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Strategy;
using SmartCopy.Core.Trash;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Provides step implementations with access to the tree root, providers, and per-node
/// transform contexts. Created once per pipeline run; contexts are cached so PathSegments
/// mutations from earlier steps are visible to later ones.
/// </summary>
public interface IStepContext
{
    DirectoryNode RootNode { get; }
    IFileSystemProvider SourceProvider { get; }
    FileSystemProviderRegistry ProviderRegistry { get; }
    ITrashService TrashService { get; }

    bool ShowHiddenFiles { get; }
    bool AllowDeleteReadOnly { get; }

    /// <summary>Returns the cached (or newly created) <see cref="PipelineContext"/> for a node.
    /// PathSegments mutations persist across all steps in the run.</summary>
    PipelineContext GetNodeContext(DirectoryTreeNode node);

    OperationalSettings OperationalSettings { get; }

    bool IsNodeFailed(DirectoryTreeNode node);
    void MarkFailed(DirectoryTreeNode node);

    /// <summary>The policy used to resolve copy strategies. Defaults to the static policy;
    /// the runner overrides it with the per-job policy.</summary>
    ICopyStrategyPolicy CopyStrategyPolicy => DefaultCopyStrategyPolicy.Instance;

    /// <summary>
    /// Resolves the <see cref="ICopyStrategy"/> for transferring files to <paramref name="targetProvider"/>,
    /// from the source/destination drive classifications, same-volume status, and provider capabilities.
    /// </summary>
    async ValueTask<ICopyStrategy> ResolveCopyStrategyAsync(IFileSystemProvider targetProvider, CancellationToken ct)
    {
        var source = await SourceProvider.GetClassificationAsync(ct);
        var target = await targetProvider.GetClassificationAsync(ct);
        var sourceVolumeId = SourceProvider.VolumeId;
        var targetVolumeId = targetProvider.VolumeId;
        var sameVolume = sourceVolumeId is { } vid && targetVolumeId == vid;
        
        return CopyStrategyPolicy.Resolve(
            new CopyStrategyInputs(
                OperationalSettings, 
                source, 
                target,
                SourceProvider.Capabilities, 
                targetProvider.Capabilities, 
                sameVolume,
                sourceVolumeId,
                targetVolumeId)
                );
    }
}
