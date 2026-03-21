namespace SmartCopy.Core.FileSystem;

public sealed class FileSystemProviderRegistry : IPathResolver, IDisposable
{
    // Instance state — per-registry registered providers
    private readonly System.Threading.Lock _lock = new();
    private readonly Dictionary<string, IFileSystemProvider> _registered
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<string, IFileSystemProvider?>> _schemeFactories
        = new(StringComparer.OrdinalIgnoreCase);

    // Static state — safe: LocalFileSystemProvider is stateless, shared across all registries
    private static readonly System.Threading.Lock LocalSync = new();
    private static readonly Dictionary<string, LocalFileSystemProvider> LocalProviders
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A shared empty registry for local-path-only resolution.</summary>
    public static FileSystemProviderRegistry Empty { get; } = new();

    /// <summary>Register a file system provider for a given root.</summary>
    public void Register(IFileSystemProvider provider)
    {
        string rootPath = provider.RootPath;
        if (string.IsNullOrEmpty(rootPath))
            throw new ArgumentException("Registering a provider without a root path");
        
        lock (_lock)
        {
            if (_registered.ContainsKey(rootPath))
                throw new InvalidOperationException($"Provider already registered for this root path: {rootPath}");

            _registered[rootPath] = provider;
        }
    }

    /// <summary>Register a factory that lazily creates a provider for a URI scheme (e.g. "mtp", "mem").</summary>
    public void RegisterSchemeFactory(string scheme, Func<string, IFileSystemProvider?> factory)
    {
        lock (_lock)
            _schemeFactories[scheme] = factory;
    }

    /// <summary>Unregister a file system provider.</summary>
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
    public IFileSystemProvider? ResolveProvider(string path)
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
            return best;

        // URI-scheme paths (mtp://, mem://, etc.) must never fall through to the local filesystem.
        // Try a registered scheme factory, or return null if none is registered for this scheme.
        var schemeEnd = path.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd >= 0)
        {
            var scheme = path[..schemeEnd];
            Func<string, IFileSystemProvider?>? factory;
            lock (_lock)
                _schemeFactories.TryGetValue(scheme, out factory);

            if (factory is null)
                return null;

            var created = factory(path);
            if (created is not null)
                Register(created);
            return created;
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

        lock (LocalSync)
        {
            if (!LocalProviders.TryGetValue(fullPath, out var provider))
            {
                LocalProviders[fullPath] = provider = new LocalFileSystemProvider(fullPath);
            }

            return provider;
        }
    }

    public void Dispose()
    {
        KeyValuePair<string, IFileSystemProvider>[] snapshot;
        lock (_lock)
        {
            snapshot = [.. _registered];
            _registered.Clear();
        }
        foreach (var entry in snapshot)
            (entry.Value as IDisposable)?.Dispose();
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
