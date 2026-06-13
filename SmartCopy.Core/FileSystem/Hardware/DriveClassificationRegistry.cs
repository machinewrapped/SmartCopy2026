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

        var task = _cache.GetOrAdd(key, k => 
        {
            var innerTask = CrossPlatformDriveClassifier.ClassifyAsync(rootPath, CancellationToken.None);
            
            // Evict faulted/canceled tasks so subsequent calls can retry
            _ = innerTask.ContinueWith(t => 
            {
                if (t.IsFaulted || t.IsCanceled)
                {
                    ((ICollection<KeyValuePair<string, Task<DriveClassification>>>)_cache)
                        .Remove(new KeyValuePair<string, Task<DriveClassification>>(k, innerTask));
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            return innerTask;
        });

        return await task.WaitAsync(ct).ConfigureAwait(false);
    }
}
