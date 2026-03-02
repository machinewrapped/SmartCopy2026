using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Filters.Filters;

public enum MirrorCompareMode
{
    NameOnly,
    NameAndSize,
}

public sealed class MirrorFilter : FilterBase
{
    public MirrorFilter(
        string comparisonPath,
        MirrorCompareMode compareMode,
        FilterMode mode,
        bool isEnabled = true)
        : base("Mirror", mode, isEnabled)
    {
        ComparisonPath = comparisonPath;
        CompareMode = compareMode;
    }

    public string ComparisonPath { get; }
    public MirrorCompareMode CompareMode { get; }
    public override bool AppliesToDirectories => true;

    public override string Summary => "Skip files already mirrored";
    public override string Description => $"Mirror: {ComparisonPath} ({CompareMode})";

    public override async ValueTask<bool> MatchesAsync(
        DirectoryTreeNode node,
        CancellationToken ct = default)
    {
        var comparisonProvider = ResolveComparisonProvider(node);
        if (comparisonProvider is null)
        {
            return false;
        }

        var comparePath = comparisonProvider.JoinPath(ComparisonPath, node.RelativePathSegments);
        var exists = await comparisonProvider.ExistsAsync(comparePath, ct);
        if (!exists)
        {
            return false;
        }

        if (CompareMode == MirrorCompareMode.NameOnly && !node.IsDirectory)
        {
            return true;
        }

        if (node.IsDirectory)
        {
            // A directory is only "mirrored" if every file directly inside it also exists
            // (and matches) in the mirror. Sub-directory contents are propagated bottom-up
            // by the filter chain infrastructure, so we only need to check direct files here.
            foreach (var file in node.Files)
            {
                var fileMirrorPath = comparisonProvider.JoinPath(ComparisonPath, file.RelativePathSegments);
                if (!await comparisonProvider.ExistsAsync(fileMirrorPath, ct))
                    return false;

                if (CompareMode == MirrorCompareMode.NameAndSize)
                {
                    var mirrorFile = await comparisonProvider.GetNodeAsync(fileMirrorPath, ct);
                    if (mirrorFile.Size != file.Size)
                        return false;
                }
            }
            return true;
        }

        var targetNode = await comparisonProvider.GetNodeAsync(comparePath, ct);
        return string.Equals(targetNode.Name, node.Name, StringComparison.OrdinalIgnoreCase)
               && targetNode.Size == node.Size;
    }

    private IFileSystemProvider? ResolveComparisonProvider(DirectoryTreeNode node)
    {
        if (string.IsNullOrWhiteSpace(ComparisonPath))
            return null;

        if (FileSystemProviderRegistry.TryResolveRegistered(ComparisonPath, out var registered))
            return registered;

        if (LooksLikeMemoryProviderPath(ComparisonPath))
        {
            return node.Provider is MemoryFileSystemProvider
                ? node.Provider
                : null;
        }

        if (Path.IsPathFullyQualified(ComparisonPath))
        {
            return FileSystemProviderRegistry.GetOrCreateLocalProvider(ComparisonPath);
        }

        return node.Provider;
    }

    private static bool LooksLikeMemoryProviderPath(string path)
    {
        var canonical = path.Replace('\\', '/').Trim();
        return canonical.Equals("/mem", StringComparison.OrdinalIgnoreCase) ||
               canonical.StartsWith("/mem/", StringComparison.OrdinalIgnoreCase);
    }

    protected override JsonObject BuildParameters() =>
        new()
        {
            ["comparisonPath"] = ComparisonPath,
            ["compareMode"] = CompareMode.ToString(),
        };
}
