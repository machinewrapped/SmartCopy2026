using System.Text.Json.Nodes;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Filters.Filters;

public sealed class AttributeFilter : FilterBase
{
    public AttributeFilter(FileAttributes requiredAttributes, FilterMode mode, bool isEnabled = true)
        : base("Attribute", mode, isEnabled)
    {
        RequiredAttributes = requiredAttributes;
    }

    public FileAttributes RequiredAttributes { get; }

    public override string Summary => $"Attributes include: {RequiredAttributes}";
    public override string Description => $"Attributes: {RequiredAttributes}";

    public override ValueTask<bool> MatchesAsync(
        DirectoryTreeNode node,
        IPathResolver context,
        CancellationToken ct = default)
    {
        return ValueTask.FromResult((node.Attributes & RequiredAttributes) == RequiredAttributes);
    }

    protected override JsonObject BuildParameters() =>
        new() { ["attributes"] = RequiredAttributes.ToString() };
}
