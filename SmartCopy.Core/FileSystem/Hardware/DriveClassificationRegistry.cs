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
            _ = innerTask.ContinueWith(
                _ => Evict(k, innerTask),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return innerTask;
        });

        // A task that failed before GetOrAdd published it was not in the dictionary when its
        // continuation ran, so the eviction above missed it.
        Evict(key, task);

        try
        {
            return await task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (DriveClassificationTimeoutException)
        {
            ct.ThrowIfCancellationRequested();
            return DriveClassification.Unknown;
        }
    }

    /// <summary>Discards <paramref name="task"/> if it failed, leaving the key free for a retry.</summary>
    private static void Evict(string key, Task<DriveClassification> task)
    {
        if (!task.IsCompleted || task.IsCompletedSuccessfully) return;

        ((ICollection<KeyValuePair<string, Task<DriveClassification>>>)_cache)
            .Remove(new KeyValuePair<string, Task<DriveClassification>>(key, task));
    }
}
