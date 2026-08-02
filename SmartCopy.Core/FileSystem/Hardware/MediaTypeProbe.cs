using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;

namespace SmartCopy.Core.FileSystem.Hardware;

/// <summary>
/// Determines rotational versus solid-state media when the OS will not say — USB mass-storage bridges
/// do not pass a device's rotation rate to the host, so on macOS neither <c>diskutil</c> nor IOKit
/// reports it for an external drive. Two fallbacks, cheapest first: the device's own product name,
/// then a random-read latency measurement. The measurements behind the thresholds below are recorded
/// in <c>MediaTypeProbeTests</c>.
/// </summary>
internal static class MediaTypeProbe
{
    /// <summary>A read at or above this took a mechanical seek. Solid state does not produce them:
    /// a USB-attached SSD's worst sample was 2.0ms, against 30-55ms maxima on a rotational drive.</summary>
    internal const double SeekLatencyFloorMs = 3.0;

    /// <summary>Ceiling on the median for a solid-state verdict, once seeks have been ruled out.</summary>
    internal const double SsdMedianCeilingMs = 1.5;

    /// <summary>Proportion of samples showing a seek that condemns the drive as rotational. Seeks are
    /// counted rather than averaged because the OS caches blocks read by anything else, which can pull
    /// a rotational drive's median down into solid-state range; caching hides reads but cannot invent
    /// a 30ms seek.</summary>
    private const double RotationalSeekFraction = 0.25;

    /// <summary>Allows a bus or scheduler hiccup without clearing a drive on a lucky sample.</summary>
    private const int MaxSolidStateOutliers = 1;

    internal const int SampleBlockBytes = 4096;
    internal const int TargetSampleCount = 24;

    /// <summary>Below this no verdict is given: the rotational threshold is proportional, so a small
    /// sample lets ordinary I/O contention supply enough slow reads to convict a solid-state drive.</summary>
    internal const int MinimumSampleCount = 16;

    /// <summary>Sampling stops here on a rotational verdict, the slow case. A solid-state verdict reads
    /// the full <see cref="TargetSampleCount"/> — stopping early would clear a drive whose opening
    /// samples happened to come from cache, and the reads saved are the fast ones.</summary>
    private const int DecisiveSampleCount = 16;

    /// <summary>Bounds the search for sample files so an unreadable or vast tree cannot stall a scan.</summary>
    private const int MaxDirectoriesVisited = 64;

    /// <summary>
    /// Bounds the entire latency probe. Individual filesystem calls are synchronous and cannot be
    /// interrupted reliably, so the caller stops waiting when this expires while the worker observes
    /// the canceled token at its next safe boundary.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Spreads samples across the tree; files in one directory tend to be allocated together,
    /// which would let a rotational drive answer without seeking.</summary>
    private const int MaxFilesPerDirectory = 4;

    /// <summary>Keyed by mount point, so the measurement is paid once per volume rather than once per
    /// scanned folder. Only definitive results are cached; an inconclusive probe may have run against
    /// an empty folder.</summary>
    private static readonly ConcurrentDictionary<string, DriveMediaType> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Reads the media type out of a device's product name, which vendors often state outright
    /// ("SanDisk Portable SSD Media"). Returns Unknown for names that say nothing ("Seagate BUP BK").
    /// </summary>
    internal static DriveMediaType FromDeviceName(string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return DriveMediaType.Unknown;

        if (ContainsAcronym(deviceName, "SSD") ||
            ContainsAcronym(deviceName, "NVME") ||
            deviceName.Contains("Solid State", StringComparison.OrdinalIgnoreCase))
            return DriveMediaType.SSD;

        if (ContainsAcronym(deviceName, "HDD") ||
            deviceName.Contains("Hard Drive", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Contains("Hard Disk", StringComparison.OrdinalIgnoreCase))
            return DriveMediaType.HDD;

        return DriveMediaType.Unknown;
    }

    /// <summary>Matches an acronym that is not embedded in a lowercase word, so "Portable SSD" and
    /// "PSSD" both match while "CrossDrive" does not.</summary>
    private static bool ContainsAcronym(string value, string acronym)
    {
        int index = value.IndexOf(acronym, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            bool precededByWord = index > 0 && char.IsLower(value[index - 1]);
            int after = index + acronym.Length;
            bool followedByWord = after < value.Length && char.IsLower(value[after]);

            if (!precededByWord && !followedByWord) return true;

            index = value.IndexOf(acronym, index + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>Measures random-read latency under <paramref name="searchRoots"/> and classifies the
    /// result. Read-only, and bounded to <see cref="TargetSampleCount"/> single-block reads.</summary>
    internal static async Task<DriveMediaType> MeasureAsync(
        string volumeKey,
        IReadOnlyList<string> searchRoots,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(volumeKey) && _cache.TryGetValue(volumeKey, out var cached))
            return cached;

        var mediaType = await RunWithTimeoutAsync(timeoutCt =>
        {
            var files = CollectSampleFiles(searchRoots, timeoutCt);
            return ClassifyLatencies(SampleLatencies(files, timeoutCt));
        }, ProbeTimeout, ct).ConfigureAwait(false);

        if (mediaType != DriveMediaType.Unknown && !string.IsNullOrEmpty(volumeKey))
            _cache[volumeKey] = mediaType;

        return mediaType;
    }

    /// <summary>
    /// Runs synchronous probe work without allowing an uninterruptible filesystem call to hold the
    /// caller indefinitely. Caller cancellation still propagates; expiration is reported as transient
    /// so the classification registry can return Unknown without caching the failed attempt.
    /// </summary>
    internal static async Task<DriveMediaType> RunWithTimeoutAsync(
        Func<CancellationToken, DriveMediaType> probe,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        var probeTask = Task.Run(() => probe(timeoutCts.Token), timeoutCts.Token);
        try
        {
            return await probeTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Observe a late failure after the timed-out worker returns from its synchronous I/O.
            _ = probeTask.ContinueWith(
                task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw new DriveClassificationTimeoutException();
        }
    }

    /// <summary>
    /// Classifies on how many samples show a seek, not on the average latency — see
    /// <see cref="RotationalSeekFraction"/>.
    /// </summary>
    internal static DriveMediaType ClassifyLatencies(IReadOnlyList<double> latenciesMs)
    {
        if (latenciesMs.Count < MinimumSampleCount) return DriveMediaType.Unknown;

        int seekSamples = 0;
        foreach (var latency in latenciesMs)
        {
            if (latency >= SeekLatencyFloorMs) seekSamples++;
        }

        if (seekSamples >= latenciesMs.Count * RotationalSeekFraction) return DriveMediaType.HDD;

        if (seekSamples <= MaxSolidStateOutliers && Median(latenciesMs) <= SsdMedianCeilingMs)
            return DriveMediaType.SSD;

        // Too few seeks to condemn it, too slow to clear it: say nothing.
        return DriveMediaType.Unknown;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.ToArray();
        Array.Sort(sorted);
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    /// <summary>Bounds the cost of a directory holding thousands of entries.</summary>
    private const int MaxEntriesPerDirectory = 512;

    /// <summary>Caps elapsed time as well as work: each entry costs a lazy stat on Unix, so the count
    /// limits alone permit tens of thousands of them on a cold drive.</summary>
    private static readonly TimeSpan SearchBudget = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Breadth-first so samples spread across the tree, capped on directories visited, files taken per
    /// directory, entries read per directory, and elapsed time. Files smaller than one block cannot be
    /// sampled. Search roots overlap — the scanned folder lies under the mount point — so visited paths
    /// are tracked to stop the second root re-walking the first.
    /// </summary>
    private static List<string> CollectSampleFiles(IReadOnlyList<string> searchRoots, CancellationToken ct)
    {
        var files = new List<string>(TargetSampleCount);
        var seen = new HashSet<string>(PathHelper.LocalPathComparer);
        var queue = new Queue<string>();
        foreach (var root in searchRoots)
        {
            if (!string.IsNullOrWhiteSpace(root) && seen.Add(root)) queue.Enqueue(root);
        }

        var options = new EnumerationOptions { IgnoreInaccessible = true };
        int directoriesVisited = 0;
        long searchStarted = Stopwatch.GetTimestamp();

        while (queue.Count > 0 && files.Count < TargetSampleCount &&
               directoriesVisited < MaxDirectoriesVisited &&
               Stopwatch.GetElapsedTime(searchStarted) < SearchBudget)
        {
            ct.ThrowIfCancellationRequested();
            var directory = queue.Dequeue();
            directoriesVisited++;

            try
            {
                var entries = new FileSystemEnumerable<DirectoryEntry>(
                    directory,
                    (ref FileSystemEntry entry) =>
                        new DirectoryEntry(entry.ToFullPath(), entry.IsDirectory, entry.Length),
                    options);

                int takenHere = 0;
                int seenHere = 0;

                foreach (var entry in entries)
                {
                    if (++seenHere > MaxEntriesPerDirectory) break;

                    if (entry.IsDirectory)
                    {
                        if (seen.Add(entry.Path)) queue.Enqueue(entry.Path);
                        continue;
                    }

                    if (takenHere >= MaxFilesPerDirectory || entry.Length < SampleBlockBytes) continue;
                    if (!seen.Add(entry.Path)) continue;

                    files.Add(entry.Path);
                    takenHere++;
                    if (files.Count >= TargetSampleCount) break;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable directory: the probe is best-effort, keep sampling elsewhere.
            }
        }

        return files;
    }

    private readonly record struct DirectoryEntry(string Path, bool IsDirectory, long Length);

    private static List<double> SampleLatencies(IReadOnlyList<string> files, CancellationToken ct)
    {
        var latencies = new List<double>(files.Count);
        var buffer = new byte[SampleBlockBytes];
        var random = new Random();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                using var handle = File.OpenHandle(
                    file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                long length = RandomAccess.GetLength(handle);
                if (length < SampleBlockBytes) continue;

                // A random block-aligned offset makes a rotational drive seek.
                long offset = random.NextInt64(length - SampleBlockBytes + 1) & ~(SampleBlockBytes - 1L);

                long start = Stopwatch.GetTimestamp();
                int bytesRead = RandomAccess.Read(handle, buffer, offset);
                var elapsed = Stopwatch.GetElapsedTime(start);

                if (bytesRead <= 0) continue;

                latencies.Add(elapsed.TotalMilliseconds);

                if (latencies.Count >= DecisiveSampleCount &&
                    ClassifyLatencies(latencies) == DriveMediaType.HDD)
                    break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Locked or vanished file: skip it.
            }
        }

        return latencies;
    }
}
