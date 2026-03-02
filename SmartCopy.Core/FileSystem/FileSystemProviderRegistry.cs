using System;
using System.Collections.Generic;
using System.IO;

namespace SmartCopy.Core.FileSystem;

public static class FileSystemProviderRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, IFileSystemProvider> RegisteredProviders = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, LocalFileSystemProvider> LocalProviders = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(string pathPrefix, IFileSystemProvider provider)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix))
            throw new ArgumentException("Path prefix must be provided.", nameof(pathPrefix));
        ArgumentNullException.ThrowIfNull(provider);

        var canonicalPrefix = CanonicalizePath(pathPrefix);
        lock (Sync)
        {
            RegisteredProviders[canonicalPrefix] = provider;
        }
    }

    public static void Unregister(string pathPrefix)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix))
            return;

        var canonicalPrefix = CanonicalizePath(pathPrefix);
        lock (Sync)
        {
            RegisteredProviders.Remove(canonicalPrefix);
        }
    }

    /// <summary>
    /// Returns the best-matching registered provider, or a lazily-created
    /// <see cref="LocalFileSystemProvider"/> for any fully-qualified local path.
    /// Returns <see langword="null"/> for relative or unrecognised paths.
    /// </summary>
    public static IFileSystemProvider? Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (TryResolveRegistered(path, out var registered))
            return registered;

        if (Path.IsPathFullyQualified(path))
            return GetOrCreateLocalProvider(path);

        return null;
    }

    public static bool TryResolveRegistered(string path, out IFileSystemProvider provider)
    {
        provider = null!;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        var canonicalPath = CanonicalizePath(path);
        KeyValuePair<string, IFileSystemProvider>? bestMatch = null;

        lock (Sync)
        {
            foreach (var entry in RegisteredProviders)
            {
                if (!IsPrefixMatch(canonicalPath, entry.Key))
                    continue;

                if (bestMatch is null || entry.Key.Length > bestMatch.Value.Key.Length)
                    bestMatch = entry;
            }
        }

        if (bestMatch is null)
            return false;

        provider = bestMatch.Value.Value;
        return true;
    }

    public static IFileSystemProvider GetOrCreateLocalProvider(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        var providerRoot = string.IsNullOrWhiteSpace(root) ? fullPath : root;

        lock (Sync)
        {
            if (!LocalProviders.TryGetValue(providerRoot, out var provider))
            {
                provider = new LocalFileSystemProvider(providerRoot);
                LocalProviders[providerRoot] = provider;
            }

            return provider;
        }
    }

    private static bool IsPrefixMatch(string canonicalPath, string canonicalPrefix)
    {
        if (canonicalPath.Equals(canonicalPrefix, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!canonicalPath.StartsWith(canonicalPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        if (canonicalPrefix.EndsWith("/", StringComparison.Ordinal))
            return true;

        return canonicalPath.Length > canonicalPrefix.Length
               && canonicalPath[canonicalPrefix.Length] == '/';
    }

    private static string CanonicalizePath(string path)
    {
        var canonical = path.Trim().Replace('\\', '/');
        while (canonical.Contains("//", StringComparison.Ordinal))
        {
            canonical = canonical.Replace("//", "/", StringComparison.Ordinal);
        }

        if (canonical.Length == 0)
            return "/";

        // Keep root-like paths intact ("/", "C:/"), trim trailing separators otherwise.
        if (canonical.Length == 1 && canonical[0] == '/')
            return canonical;

        if (canonical.Length == 3 && char.IsLetter(canonical[0]) && canonical[1] == ':' && canonical[2] == '/')
            return canonical;

        return canonical.TrimEnd('/');
    }
}
