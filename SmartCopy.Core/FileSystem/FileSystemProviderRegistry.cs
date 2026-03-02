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

    // <summary>Register a file system provider for a given root
    public void Register(IFileSystemProvider provider)
    {
        string rootPath = provider.RootPath;
        if (string.IsNullOrEmpty(rootPath))
            throw new ArgumentException("Registering a provider without a root path");
        
        lock (_lock)
        {
            if (_registered.Keys.Contains(rootPath))
                throw new InvalidOperationException($"Provider already registered for this root path: {rootPath}");

            _registered[rootPath] = provider;
        }
    }

    // <summary>Register a file system provider
    public void Unregister(IFileSystemProvider provider)
    {
        string rootPath = provider.RootPath;
        if (string.IsNullOrEmpty(rootPath))
            throw new ArgumentException("Registering a provider without a root path");
        
        lock (_lock)
        {
            _registered.Remove(rootPath);
        }
    }

    /// <summary>
    /// Resolves a provider for <paramref name="path"/> using longest-prefix matching
    /// among registered providers, falling back to a cached <see cref="LocalFileSystemProvider"/>
    /// for fully-qualified local paths.
    /// </summary>
    public IFileSystemProvider? Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        // Snapshot under lock, then search outside lock
        KeyValuePair<string, IFileSystemProvider>[] snapshot;
        lock (_lock)
            snapshot = [.. _registered];

        IFileSystemProvider? best = null;
        foreach (var entry in snapshot)
        {
            IFileSystemProvider provider = entry.Value;

            var rootSegments = provider.SplitPath(entry.Key);

            // Some providers represent their own root as zero segments (e.g. "/mem").
            // Keep a fast textual check for that case.
            if (rootSegments.Length == 0)
            {
                if (!IsPrefixMatch(path, entry.Key))
                    continue;
            }
            else
            {
                var pathSegments = provider.SplitPath(path);
                if (pathSegments.Length < rootSegments.Length)
                    continue;

                bool matches = true;
                for (int i = 0; i < rootSegments.Length; i++)
                {
                    if (!string.Equals(pathSegments[i], rootSegments[i], StringComparison.OrdinalIgnoreCase))
                    {
                        matches = false;
                        break;
                    }
                }

                if (!matches)
                    continue;
            }

            if (best is null || entry.Key.Length > best.RootPath.Length)
            {
                best = provider;
            }
        }

        if (best is not null)
        {
            return best;
        }

        return GetOrCreateLocalProvider(path);
    }

    /// <summary>
    /// Returns (or lazily creates) a <see cref="LocalFileSystemProvider"/> for the drive
    /// root of <paramref name="path"/>. Returns <see langword="null"/> for non-local paths.
    /// </summary>
    public static IFileSystemProvider? GetOrCreateLocalProvider(string path)
    {
        if (!Path.IsPathFullyQualified(path)) 
            return null;

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        var providerRoot = string.IsNullOrWhiteSpace(root) ? fullPath : root;

        lock (LocalSync)
        {
            if (!LocalProviders.TryGetValue(providerRoot, out var provider))
            {
                LocalProviders[providerRoot] = provider = new LocalFileSystemProvider(providerRoot);
            }

            return provider;
        }
    }

    internal static bool IsPrefixMatch(string path, string prefix)
    {
        if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        return path.Length > prefix.Length;
    }
}
