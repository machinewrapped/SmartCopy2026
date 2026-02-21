using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Filters.Filters;

public sealed class ExtensionFilter : FilterBase
{
    private readonly HashSet<string> _extensions;

    public ExtensionFilter(IEnumerable<string> extensions, FilterMode mode, bool isEnabled = true)
        : base("Extension", mode, isEnabled)
    {
        var normalized = extensions
            .Select(extension => extension.Trim().TrimStart('.'))
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => extension.ToLowerInvariant())
            .ToArray();

        _extensions = new HashSet<string>(normalized);
        Extensions = normalized;
    }

    public IReadOnlyList<string> Extensions { get; }

    public override string Summary => $"Extensions: {string.Join(", ", Extensions.Select(e => "." + e))}";
    public override string Description => $"Extension: {string.Join("; ", Extensions.Select(e => "*." + e))}";

    public override bool Matches(FileSystemNode node, IFileSystemProvider? comparisonProvider)
    {
        if (node.IsDirectory)
        {
            return false;
        }

        var extension = Path.GetExtension(node.Name).TrimStart('.').ToLowerInvariant();
        return _extensions.Contains(extension);
    }

    protected override JsonObject BuildParameters() =>
        new() { ["extensions"] = string.Join(';', Extensions) };
}

