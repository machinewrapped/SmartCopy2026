using System;
using System.IO;

namespace SmartCopy.Core.FileSystem;

internal static class PathHelper
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Path.DirectorySeparatorChar.ToString();
        }

        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static string EnsureTrailingSeparator(string path)
    {
        if (path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar))
        {
            return path;
        }

        return path + Path.DirectorySeparatorChar;
    }

    public static string GetRelativePath(string rootPath, string fullPath)
    {
        var root = EnsureTrailingSeparator(Normalize(rootPath));
        var full = Normalize(fullPath);
        var relative = Path.GetRelativePath(root, full);
        return relative == "." ? string.Empty : relative;
    }

    public static string CombineForProvider(string basePath, string pathFragment)
    {
        if (Path.IsPathFullyQualified(pathFragment))
        {
            return Normalize(pathFragment);
        }

        if (string.IsNullOrWhiteSpace(basePath))
        {
            return Normalize(pathFragment);
        }

        return Normalize(Path.Combine(basePath, pathFragment));
    }
}

