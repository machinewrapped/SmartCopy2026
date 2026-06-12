using System;
using System.Collections.Concurrent;

namespace SmartCopy.Core.FileSystem.Hardware;

public static class DriveClassificationRegistry
{
    private static readonly ConcurrentDictionary<string, Task<DriveClassification>> _cache 
        = new(StringComparer.Ordinal);

    public static async ValueTask<DriveClassification> GetOrClassifyAsync(string rootPath, string? volumeId, CancellationToken ct = default)
    {
        string key = string.IsNullOrWhiteSpace(volumeId) ? rootPath : volumeId;

        // Note: Task caching. If the classification fails, we might want to remove it from cache
        // but for now, we'll cache the result of the hardware query.
        return await _cache.GetOrAdd(key, _ => CrossPlatformDriveClassifier.ClassifyAsync(rootPath, ct));
    }
}
