using System.IO;
using System.Text.Json.Nodes;
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

    public override bool Matches(FileSystemNode node, IFileSystemProvider? comparisonProvider)
    {
        return (node.Attributes & RequiredAttributes) == RequiredAttributes;
    }

    protected override JsonObject BuildParameters() =>
        new() { ["attributes"] = RequiredAttributes.ToString() };
}

