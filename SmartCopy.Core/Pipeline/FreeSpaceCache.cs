using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Tracks cached free-space values per filesystem volume, keyed by volume ID or root path.
/// Mutable; individual pipeline steps and validation passes reduce the cache as they
/// account for bytes they will consume, allowing cumulative space checks across steps.
/// </summary>
public sealed class FreeSpaceCache
{
    private readonly Dictionary<string, long?> _cache;

    public FreeSpaceCache()
    {
        _cache = new Dictionary<string, long?>();
    }

    /// <summary>Initialises a mutable copy from an existing cache snapshot.</summary>
    public FreeSpaceCache(FreeSpaceCache source)
    {
        _cache = new Dictionary<string, long?>(source._cache);
    }

    // -------------------------------------------------------------------------
    // Core dictionary operations
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the cached free-space value for <paramref name="provider"/>,
    /// or <see langword="null"/> when not present or the provider cannot be queried.
    /// </summary>
    public long? GetForProvider(IFileSystemProvider provider)
    {
        if (!provider.Capabilities.CanQueryFreeSpace) return null;

        var key = KeyFor(provider);
        if (key is null) return null;

        return _cache.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Stores (or overwrites) the cached free-space value for <paramref name="provider"/>.
    /// </summary>
    public void SetForProvider(IFileSystemProvider provider, long? freeSpace)
    {
        var key = KeyFor(provider);
        if (key is null) return;

        _cache[key] = freeSpace;
    }

    /// <summary>
    /// Decrements the cached free-space for <paramref name="provider"/> by <paramref name="bytes"/>,
    /// flooring at zero. No-op when the entry is absent or already null.
    /// </summary>
    public void ReduceForProvider(IFileSystemProvider provider, long bytes)
    {
        var key = KeyFor(provider);
        if (key is null) return;

        if (_cache.TryGetValue(key, out var freeSpace) && freeSpace is not null)
        {
            _cache[key] = Math.Max(0, freeSpace.Value - bytes);
        }
    }

    /// <summary>
    /// Decrements the cached free-space for the provider of <paramref name="path"/> by <paramref name="bytes"/>,
    /// flooring at zero. No-op when the entry is absent or already null.
    /// </summary>
    public void ReduceForPath(IPathResolver resolver, string path, long bytes)
    {
        IFileSystemProvider? provider = resolver.ResolveProvider(path);
        if (provider == null) return;

        ReduceForProvider(provider, bytes);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the cache already contains an entry for the given key,
    /// regardless of whether the stored value is null.
    /// </summary>
    public bool ContainsKeyFor(IFileSystemProvider provider)
    {
        var key = KeyFor(provider);
        return key is not null && _cache.ContainsKey(key);
    }

    // -------------------------------------------------------------------------
    // Higher-level async helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Queries and caches free space for the destination provider referenced by
    /// <paramref name="destination"/>. Skips the query when the key is already present.
    /// </summary>
    public async Task CacheForDestinationAsync(
        IHasDestinationPath destination,
        IPathResolver pathResolver,
        CancellationToken ct = default)
    {
        if (destination.DestinationPath is null) return;

        var target = pathResolver.ResolveProvider(destination.DestinationPath);
        if (target is null) return;
        if (!target.Capabilities.CanQueryFreeSpace) return;

        var key = KeyFor(target);
        if (key is null) return;

        if (_cache.ContainsKey(key)) return;

        long? freeSpace = await target.GetAvailableFreeSpaceAsync(ct);
        _cache[key] = freeSpace;
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="FreeSpaceCache"/> pre-populated with free-space values for
    /// every destination path referenced by the given pipeline <paramref name="steps"/>.
    /// </summary>
    public static async Task<FreeSpaceCache> BuildForPipelineAsync(
        IReadOnlyList<IPipelineStep> steps,
        IPathResolver pathResolver,
        CancellationToken ct = default)
    {
        var cache = new FreeSpaceCache();
        foreach (var step in steps)
        {
            if (step is IHasDestinationPath destination)
            {
                await cache.CacheForDestinationAsync(destination, pathResolver, ct);
            }
        }

        return cache;
    }

    // -------------------------------------------------------------------------
    // Key helper
    // -------------------------------------------------------------------------

    private static string? KeyFor(IFileSystemProvider provider)
    {
        var key = provider.VolumeId ?? provider.RootPath;
        return string.IsNullOrEmpty(key) ? null : key;
    }
}
