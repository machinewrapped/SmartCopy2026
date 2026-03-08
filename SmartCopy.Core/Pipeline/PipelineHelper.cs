using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Helper class for common operations on the transform pipeline.
/// </summary>
public static class PipelineHelper
{
    public async static void CacheFreeSpaceForDestination(
        Dictionary<string, long?> freeSpaceCache,
        IHasDestinationPath destination,
        IPathResolver pathResolver,
        CancellationToken ct = default)
    {
        if (destination.DestinationPath is null) return;
        var target = pathResolver.ResolveProvider(destination.DestinationPath);

        if (target is not null && target.Capabilities.CanQueryFreeSpace)
        {
            if (freeSpaceCache.Keys.Contains(target.RootPath)) 
                return;

            long? freeSpace = await target.GetAvailableFreeSpaceAsync(ct);
            freeSpaceCache[target.RootPath] = freeSpace;
        }                        
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
                CacheFreeSpaceForDestination(cache, destination, pathResolver, ct);
            }
        }

        return cache;
    }

}