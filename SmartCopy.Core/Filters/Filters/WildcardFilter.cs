using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Filters.Filters;

public sealed class WildcardFilter : FilterBase
{
    private readonly Regex[] _patterns;

    public WildcardFilter(
        string pattern,
        FilterMode mode,
        bool isEnabled = true)
        : base("Wildcard", mode, isEnabled)
    {
        Pattern = pattern;
        _patterns = ParsePatterns(pattern);
    }

    public string Pattern { get; }

    public override string Summary => $"Name matches {Pattern}";
    public override string Description => $"Wildcard: {Pattern}";

    public override ValueTask<bool> MatchesAsync(
        DirectoryTreeNode node,
        IFileSystemProvider? comparisonProvider,
        CancellationToken ct = default)
    {
        if (_patterns.Length == 0)
        {
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(_patterns.Any(pattern => pattern.IsMatch(node.Name)));
    }

    protected override JsonObject BuildParameters() =>
        new() { ["pattern"] = Pattern };

    private static Regex[] ParsePatterns(string pattern)
    {
        return pattern
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(token => "^" + Regex.Escape(token)
                .Replace(@"\*", ".*", StringComparison.Ordinal)
                .Replace(@"\?", ".", StringComparison.Ordinal) + "$")
            .Select(regex => new Regex(regex, RegexOptions.Compiled | RegexOptions.IgnoreCase))
            .ToArray();
    }
}
