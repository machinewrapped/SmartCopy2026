using System;
using System.IO;
using System.Text.Json.Nodes;
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

    public override string Summary => "Skip files already mirrored";
    public override string Description => $"Mirror: {ComparisonPath} ({CompareMode})";

    public override bool Matches(FileSystemNode node, IFileSystemProvider? comparisonProvider)
    {
        if (comparisonProvider is null)
        {
            return false;
        }

        var comparePath = PathHelper.CombineForProvider(ComparisonPath, node.RelativePath);
        var exists = comparisonProvider.ExistsAsync(comparePath, default).GetAwaiter().GetResult();
        if (!exists)
        {
            return false;
        }

        if (CompareMode == MirrorCompareMode.NameOnly || node.IsDirectory)
        {
            return true;
        }

        var targetNode = comparisonProvider.GetNodeAsync(comparePath, default).GetAwaiter().GetResult();
        return string.Equals(targetNode.Name, node.Name, StringComparison.OrdinalIgnoreCase)
               && targetNode.Size == node.Size;
    }

    protected override JsonObject BuildParameters() =>
        new()
        {
            ["comparisonPath"] = ComparisonPath,
            ["compareMode"] = CompareMode.ToString(),
        };
}

