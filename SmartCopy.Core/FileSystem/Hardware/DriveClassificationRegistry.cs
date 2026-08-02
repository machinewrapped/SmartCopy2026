using System;
using System.Collections.Concurrent;

namespace SmartCopy.Core.FileSystem.Hardware;

public static class DriveClassificationRegistry
{
    private static readonly ConcurrentDictionary<string, Task<DriveClassification>> _cache 
        = new(StringComparer.Ordinal);

    public static ValueTask<DriveClassification> GetOrClassifyAsync(
        string rootPath,
        string? volumeId,
        CancellationToken ct = default) =>
        GetOrClassifyAsync(rootPath, volumeId, CrossPlatformDriveClassifier.ClassifyAsync, ct);

    internal static async ValueTask<DriveClassification> GetOrClassifyAsync(
        string rootPath,
        string? volumeId,
        Func<string, CancellationToken, Task<DriveClassification>> classifyAsync,
        CancellationToken ct = default)
    {
        string key = string.IsNullOrWhiteSpace(volumeId) ? rootPath : volumeId;

        var task = _cache.GetOrAdd(key, k => 
        {
            var innerTask = classifyAsync(rootPath, ct);
            
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

        try
        {
            return await task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (DriveClassificationTimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            // The continuation can run before GetOrAdd publishes a synchronously faulted task.
            // Remove again here so a timed-out attempt is never retained by that race.
            ((ICollection<KeyValuePair<string, Task<DriveClassification>>>)_cache)
                .Remove(new KeyValuePair<string, Task<DriveClassification>>(key, task));
            return DriveClassification.Unknown;
        }
    }
}
