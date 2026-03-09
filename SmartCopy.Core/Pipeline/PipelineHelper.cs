using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Helper class for common operations on the transform pipeline.
/// </summary>
public static class PipelineHelper
{
    public static string? FreeSpaceCacheKey(IFileSystemProvider provider)
        => provider.VolumeId ?? provider.RootPath;

    public static long? GetFreeSpaceCacheForProvider(IReadOnlyDictionary<string, long?> freeSpaceCache, IFileSystemProvider provider)
    {
        if (!provider.Capabilities.CanQueryFreeSpace) return null;

        var cacheKey = FreeSpaceCacheKey(provider);
        if (cacheKey is null || string.IsNullOrEmpty(cacheKey)) return null;

        if (freeSpaceCache.TryGetValue(cacheKey, out var freeSpace))
            return freeSpace;

        return null;
    }

    public static void UpdateFreeSpaceCacheForProvider(Dictionary<string, long?> freeSpaceCache, IFileSystemProvider provider, long? freeSpace)
    {
        var cacheKey = FreeSpaceCacheKey(provider);
        if (cacheKey is null) return;

        freeSpaceCache[cacheKey] = freeSpace;
    }

    public static void ReduceFreeSpaceCacheForProvider(Dictionary<string, long?> freeSpaceCache, IFileSystemProvider provider, long bytes)
    {
        var cacheKey = FreeSpaceCacheKey(provider);
        if (cacheKey is null) return;

        if (freeSpaceCache.TryGetValue(cacheKey, out var freeSpace) && freeSpace is not null)
        {
            freeSpaceCache[cacheKey] = Math.Max(0, freeSpace.Value - bytes);
        }
    }

    public async static Task CacheFreeSpaceForDestination(
        Dictionary<string, long?> freeSpaceCache,
        IHasDestinationPath destination,
        IPathResolver pathResolver,
        CancellationToken ct = default)
    {
        if (destination.DestinationPath is null) return;
        var target = pathResolver.ResolveProvider(destination.DestinationPath);
        if (target is null) return;
        if (!target.Capabilities.CanQueryFreeSpace) return;

        var cacheKey = FreeSpaceCacheKey(target);
        if (cacheKey is null) return;

        if (freeSpaceCache.ContainsKey(cacheKey)) 
            return;

        long? freeSpace = await target.GetAvailableFreeSpaceAsync(ct);
        freeSpaceCache[cacheKey] = freeSpace;
    }

    public static async Task<Dictionary<string, long?>> BuildFreeSpaceCacheForPipeline(
        IReadOnlyList<IPipelineStep> steps,
        IPathResolver pathResolver,
        CancellationToken ct = default)
    {
        Dictionary<string, long?> cache = new();
        foreach (var step in steps)
        {
            if (step is IHasDestinationPath destination)
            {
                await CacheFreeSpaceForDestination(cache, destination, pathResolver, ct);
            }
        }

        return cache;
    }

}