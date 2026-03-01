using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.DirectoryTree;
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

    public override string TypeDisplayName => "Extensions";
    public override string Summary => $"Extensions: {string.Join(", ", Extensions.Select(e => "." + e))}";
    public override string Description => $"Extension: {string.Join("; ", Extensions.Select(e => "*." + e))}";

    public override ValueTask<bool> MatchesAsync(
        DirectoryTreeNode node,
        IFileSystemProvider? comparisonProvider,
        CancellationToken ct = default)
    {
        if (node.IsDirectory)
        {
            return ValueTask.FromResult(false);
        }

        var extension = Path.GetExtension(node.Name).TrimStart('.').ToLowerInvariant();
        return ValueTask.FromResult(_extensions.Contains(extension));
    }

    protected override JsonObject BuildParameters() =>
        new() { ["extensions"] = string.Join(';', Extensions) };
}
