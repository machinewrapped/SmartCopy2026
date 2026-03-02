using System;
using System.Collections.Generic;
using System.IO;

namespace SmartCopy.Core.FileSystem;

public sealed class FileSystemProviderRegistry
{
    // Instance state — per-registry registered providers
    private readonly object _lock = new();
    private readonly Dictionary<string, IFileSystemProvider> _registered
        = new(StringComparer.OrdinalIgnoreCase);

    // Static state — safe: LocalFileSystemProvider is stateless, shared across all registries
    private static readonly object LocalSync = new();
    private static readonly Dictionary<string, LocalFileSystemProvider> LocalProviders
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A shared empty registry for local-path-only resolution.</summary>
    public static FileSystemProviderRegistry Empty { get; } = new();

    public void Register(string pathPrefix, IFileSystemProvider provider)
    {
        var key = CanonicalizePath(pathPrefix);
        lock (_lock)
            _registered[key] = provider;
    }

    public void Unregister(string pathPrefix)
    {
        var key = CanonicalizePath(pathPrefix);
        lock (_lock)
            _registered.Remove(key);
    }

    /// <summary>
    /// Resolves a provider for <paramref name="path"/> using longest-prefix matching
    /// among registered providers, falling back to a cached <see cref="LocalFileSystemProvider"/>
    /// for fully-qualified local paths.
    /// </summary>
    public IFileSystemProvider? Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        var canonical = CanonicalizePath(path);

        // Snapshot under lock, then search outside lock
        KeyValuePair<string, IFileSystemProvider>[] snapshot;
        lock (_lock)
            snapshot = [.. _registered];

        (string Prefix, IFileSystemProvider Provider)? best = null;
        foreach (var entry in snapshot)
        {
            if (!IsPrefixMatch(canonical, entry.Key)) continue;
            if (best is null || entry.Key.Length > best.Value.Prefix.Length)
                best = (entry.Key, entry.Value);
        }
        if (best is not null) return best.Value.Provider;

        return GetOrCreateLocalProvider(path);
    }

    /// <summary>
    /// Returns (or lazily creates) a <see cref="LocalFileSystemProvider"/> for the drive
    /// root of <paramref name="path"/>. Returns <see langword="null"/> for non-local paths.
    /// </summary>
    public static IFileSystemProvider? GetOrCreateLocalProvider(string path)
    {
        if (!Path.IsPathFullyQualified(path)) return null;

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        var providerRoot = string.IsNullOrWhiteSpace(root) ? fullPath : root;

        lock (LocalSync)
        {
            if (!LocalProviders.TryGetValue(providerRoot, out var provider))
                LocalProviders[providerRoot] = provider = new LocalFileSystemProvider(providerRoot);
            return provider;
        }
    }

    internal static string CanonicalizePath(string path)
    {
        var canonical = path.Trim().Replace('\\', '/');
        while (canonical.Contains("//", StringComparison.Ordinal))
            canonical = canonical.Replace("//", "/", StringComparison.Ordinal);

        if (canonical.Length == 0)
            return "/";

        if (canonical.Length == 1 && canonical[0] == '/')
            return canonical;

        if (canonical.Length == 3 && char.IsLetter(canonical[0]) && canonical[1] == ':' && canonical[2] == '/')
            return canonical;

        return canonical.TrimEnd('/');
    }

    internal static bool IsPrefixMatch(string canonicalPath, string canonicalPrefix)
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
}
