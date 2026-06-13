using System.Text.Json.Nodes;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Filters.Filters;

public enum MirrorCompareMode
{
    NameOnly,
    NameAndSize,
}

public sealed class MirrorFilter : FilterBase, IPipelineAwareFilter
{
    public MirrorFilter(
        string comparisonPath,
        MirrorCompareMode compareMode,
        FilterMode mode,
        bool isEnabled = true,
        bool useAutomaticPath = false)
        : base("Mirror", mode, isEnabled)
    {
        ComparisonPath = comparisonPath;
        CompareMode = compareMode;
        UseAutomaticPath = useAutomaticPath;
    }

    public string ComparisonPath { get; }
    public MirrorCompareMode CompareMode { get; }
    public bool UseAutomaticPath { get; }

    /// <inheritdoc cref="IPipelineAwareFilter.PipelineDestinationPath"/>
    public string? PipelineDestinationPath { get; set; }

    private string EffectiveComparisonPath =>
        UseAutomaticPath ? (PipelineDestinationPath ?? string.Empty) : ComparisonPath;

    public override bool AppliesToDirectories => true;

    public override string Summary => "Skip files already in target";
    public override string Description => UseAutomaticPath
        ? $"Mirror: (automatic) ({CompareMode})"
        : $"Mirror: {ComparisonPath} ({CompareMode})";

    public override async ValueTask<bool> MatchesAsync(
        DirectoryTreeNode node,
        IPathResolver context,
        CancellationToken ct = default)
    {
        var effectivePath = EffectiveComparisonPath;
        var comparisonProvider = ResolveComparisonProvider(context, effectivePath);
        if (comparisonProvider is null)
        {
            // No comparison path available — degrade gracefully so the filter is a no-op.
            // Return the neutral value for this mode: true for Only (don't exclude anything),
            // false for Exclude/Add (don't exclude or force-include anything).
            return UseAutomaticPath && Mode == FilterMode.Only;
        }

        var comparePath = comparisonProvider.JoinPath(effectivePath, node.RelativePathSegments);
        var exists = await comparisonProvider.ExistsAsync(comparePath, ct);

        if (!exists)
        {
            return false;
        }

        if (CompareMode == MirrorCompareMode.NameOnly && node is not DirectoryNode)
        {
            return true;
        }

        if (node is DirectoryNode dirNode)
        {
            // A directory is only "mirrored" if every file directly inside it also exists
            // (and matches) in the mirror. Sub-directory contents are propagated bottom-up
            // by the filter chain infrastructure, so we only need to check direct files here.
            foreach (var file in dirNode.Files)
            {
                var fileMirrorPath = comparisonProvider.JoinPath(effectivePath, file.RelativePathSegments);
                if (!await comparisonProvider.ExistsAsync(fileMirrorPath, ct))
                {
                    return false;
                }

                if (CompareMode == MirrorCompareMode.NameAndSize)
                {
                    var mirrorFile = await comparisonProvider.GetNodeAsync(fileMirrorPath, ct);
                    if (mirrorFile.Size != file.Size)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        var targetNode = await comparisonProvider.GetNodeAsync(comparePath, ct);
        return targetNode.Size == node.Size;
    }

    private static IFileSystemProvider? ResolveComparisonProvider(IPathResolver resolver, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return resolver.ResolveProvider(path);
    }

    protected override JsonObject BuildParameters() =>
        new()
        {
            ["comparisonPath"] = ComparisonPath,
            ["compareMode"] = CompareMode.ToString(),
            ["useAutomaticPath"] = UseAutomaticPath,
        };
}
