using System.Linq;
using System.Text.Json.Nodes;
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
            .Select(Normalize)
            .Where(ext => !string.IsNullOrWhiteSpace(ext))
            .ToArray();

        _extensions = new(normalized);
        Extensions = normalized;
    }

    /// <summary>
    /// Parses a user-supplied string into individual normalized extensions (no leading dot,
    /// lower-case). Handles glob-style patterns and semicolon-separated composite lists.
    /// Only tokens that pass <see cref="IsValidExtension"/> are returned.
    /// <example>
    /// <code>
    /// "*.mp3"              → ["mp3"]
    /// "*.mp3;*.mp4;*.mp5"  → ["mp3", "mp4", "mp5"]
    /// ".mp3"               → ["mp3"]
    /// "mp3"                → ["mp3"]
    /// </code>
    /// </example>
    /// </summary>
    public static IReadOnlyList<string> ParseExtensions(string input) =>
        input.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Select(Normalize)
             .Where(IsValidExtension)
             .ToArray();

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="normalized"/> is a structurally
    /// valid file-extension token (already stripped of any leading dot or glob prefix).
    /// A token is invalid if it is empty, contains an embedded dot (e.g. <c>x.y.z</c>),
    /// or contains characters that are illegal in file names.
    /// </summary>
    public static bool IsValidExtension(string normalized) =>
        !string.IsNullOrWhiteSpace(normalized)
        && !normalized.Contains('.')
        && normalized.IndexOfAny(Path.GetInvalidFileNameChars()) == -1;

    // Strips glob prefix (e.g. "*." or "*"), then strips a leading dot, then lowercases.
    public static string Normalize(string raw)
    {
        var s = raw.Trim();
        // Strip leading glob wildcard and optional dot: "*.xyz" → ".xyz", "*xyz" → "xyz"
        if (s.StartsWith("*.", StringComparison.Ordinal))
            s = s[2..];
        else if (s.StartsWith('*'))
            s = s[1..];
        // Strip optional leading dot: ".xyz" → "xyz"
        s = s.TrimStart('.');
        return s.ToLowerInvariant();
    }

    public IReadOnlyList<string> Extensions { get; }

    public override string TypeDisplayName => "Extensions";
    public override string Summary => $"Extensions: {string.Join(", ", Extensions.Select(e => "." + e))}";
    public override string Description => $"Extension: {string.Join("; ", Extensions.Select(e => "*." + e))}";

    public override ValueTask<bool> MatchesAsync(
        DirectoryTreeNode node,
        IPathResolver context,
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
