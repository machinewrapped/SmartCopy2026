using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Helper class for common operations on the transform pipeline.
/// </summary>
internal static class PipelineHelper
{
    internal async static void CacheFreeSpaceForDestination(
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

}