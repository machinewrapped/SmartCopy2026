using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SmartCopy.Core.FileSystem;

/// <summary>
/// Helper class for path manipulation on the host filesystem.
/// </summary>
public static class PathHelper
{
    public static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }

    public static string RemoveTrailingSeparator(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

        // Keep root paths intact (e.g. "C:\", "\\server\share\", "/").
        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root))
        {
            var trimmed = path.TrimEnd(separators);
            var rootTrimmed = root.TrimEnd(separators);
            if (PathComparer.Equals(trimmed, rootTrimmed))
            {
                return root;
            }
        }

        return path.TrimEnd(separators);
    }

    /// <summary>
    /// Normalizes user-entered source/destination paths for bookmark/MRU storage and comparison.
    /// - Canonicalizes local paths using <see cref="Path.GetFullPath(string)"/> when possible.
    /// - Canonicalizes memory-provider paths ("/mem/...") with forward slashes.
    /// - Removes trailing separators while preserving root paths.
    /// </summary>
    public static string NormalizeUserPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim();

        if (LooksLikeMemoryProviderPath(trimmed))
        {
            return NormalizeMemoryProviderPath(trimmed);
        }

        try
        {
            var full = Path.GetFullPath(trimmed);
            return RemoveTrailingSeparator(full);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return RemoveTrailingSeparator(trimmed);
        }
    }

    public static bool AreEquivalentUserPaths(string? left, string? right)
    {
        var normalizedLeft = NormalizeUserPath(left);
        var normalizedRight = NormalizeUserPath(right);
        return PathComparer.Equals(normalizedLeft, normalizedRight);
    }

    public static List<string> NormalizeDistinctUserPaths(IEnumerable<string> paths)
    {
        return paths
            .Select(NormalizeUserPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(PathComparer)
            .ToList();
    }

    private static bool LooksLikeMemoryProviderPath(string path)
    {
        var canonical = path.Replace('\\', '/').Trim();
        return canonical.Equals("/mem", StringComparison.OrdinalIgnoreCase) ||
               canonical.StartsWith("/mem/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeMemoryProviderPath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        if (!normalized.StartsWith('/'))
        {
            normalized = "/" + normalized;
        }

        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized.Length > 1
            ? normalized.TrimEnd('/')
            : normalized;
    }
}
