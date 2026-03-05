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
    /// - Removes trailing separators while preserving root paths.
    /// </summary>
    public static string NormalizeUserPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim();

        // If the path looks like a posix path but the OS does not use forward slashes,
        // fall back to manual normalization.
        if (LooksLikePosixPath(trimmed) && Path.PathSeparator != '/')
        {
            return NormalizePosixPath(trimmed);
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

    private static bool LooksLikePosixPath(string path)
    {
        return path.StartsWith("/", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePosixPath(string path)
    {
        if (!LooksLikePosixPath(path))
        {
            throw new ArgumentException($"Path '{path}' is not a posix-style path.", nameof(path));
        }

        path = path.Trim();

        while (path.Contains("//", StringComparison.Ordinal))
        {
            path = path.Replace("//", "/", StringComparison.Ordinal);
        }

        return path.Length > 1 ? path.TrimEnd('/') : path;
    }

    /// <summary>
    /// Gets a user-friendly name for a destination path.
    /// Returns the last segment of the path, or "destination" if the path is empty.
    /// </summary>
    public static string GetFriendlyTarget(string? path)
    {
        var normalized = NormalizeUserPath(path);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "destination";
        }

        var leaf = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(leaf) ? normalized : leaf;
    }
}
