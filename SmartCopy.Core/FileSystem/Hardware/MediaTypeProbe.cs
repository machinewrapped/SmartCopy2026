using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SmartCopy.Core.FileSystem.Hardware;

/// <summary>
/// Determines rotational versus solid-state media when the OS will not say. USB mass-storage bridges
/// do not pass a device's rotation rate to the host, so for an external drive on macOS neither
/// <c>diskutil</c> nor IOKit reports it — only the bus is known. Two fallbacks, cheapest first: the
/// device's own product name, then a random-read latency measurement. Seek time separates the two
/// classes by an order of magnitude; measured medians over 24 samples were 0.04 ms (internal NVMe),
/// 1.0 ms (USB-attached SSD) and 10.0 ms (USB-attached HDD).
/// </summary>
internal static class MediaTypeProbe
{
    /// <summary>Median at or below this is solid state; at or above <see cref="HddMedianFloorMs"/> is
    /// rotational. The gap between them stays Unknown rather than forcing a call.</summary>
    internal const double SsdMedianCeilingMs = 1.5;
    internal const double HddMedianFloorMs = 3.0;

    internal const int SampleBlockBytes = 4096;
    internal const int TargetSampleCount = 24;

    /// <summary>Below this the median is too noisy to classify on.</summary>
    internal const int MinimumSampleCount = 8;

    /// <summary>
    /// Once this many samples agree on a verdict the rest are skipped. Rotational media is both the
    /// slow case and the one that reaches a verdict early, so this bounds the worst case: a full
    /// 24-sample run on a spinning drive costs seconds, most of it spent confirming what the first
    /// dozen seeks already showed.
    /// </summary>
    private const int DecisiveSampleCount = 12;

    /// <summary>Bounds the search for sample files so an unreadable or vast tree cannot stall a scan.</summary>
    private const int MaxDirectoriesVisited = 64;

    /// <summary>Spreads samples across the tree; files in one directory tend to be allocated together,
    /// which would let a rotational drive answer without seeking.</summary>
    private const int MaxFilesPerDirectory = 4;

    /// <summary>Keyed by mount point, so the measurement is paid once per volume rather than once per
    /// scanned folder. Only definitive results are cached; an inconclusive probe may have run against
    /// an empty folder, so it is left to be retried.</summary>
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

    /// <summary>
    /// Matches an acronym that is not embedded in a lowercase word, so "Portable SSD" and "PSSD" both
    /// match while "CrossDrive" does not.
    /// </summary>
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

    /// <summary>
    /// Measures random-read latency under <paramref name="searchRoots"/> and classifies the result.
    /// Read-only, and bounded: at most <see cref="TargetSampleCount"/> single-block reads.
    /// </summary>
    internal static async Task<DriveMediaType> MeasureAsync(
        string volumeKey,
        IReadOnlyList<string> searchRoots,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(volumeKey) && _cache.TryGetValue(volumeKey, out var cached))
            return cached;

        var mediaType = await Task.Run(() =>
        {
            var files = CollectSampleFiles(searchRoots, ct);
            return ClassifyLatencies(SampleLatencies(files, ct));
        }, ct).ConfigureAwait(false);

        if (mediaType != DriveMediaType.Unknown && !string.IsNullOrEmpty(volumeKey))
            _cache[volumeKey] = mediaType;

        return mediaType;
    }

    internal static DriveMediaType ClassifyLatencies(IReadOnlyList<double> latenciesMs)
    {
        if (latenciesMs.Count < MinimumSampleCount) return DriveMediaType.Unknown;

        double median = Median(latenciesMs);
        if (median <= SsdMedianCeilingMs) return DriveMediaType.SSD;
        if (median >= HddMedianFloorMs) return DriveMediaType.HDD;
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

    /// <summary>
    /// Breadth-first so samples spread across the tree, capped on directories visited, files taken per
    /// directory, and entries read per directory. Files smaller than one block cannot be sampled.
    /// </summary>
    /// <remarks>
    /// Enumerated in a single pass reading sizes straight off <see cref="FileSystemEntry"/>. Walking
    /// with <c>DirectoryInfo.EnumerateFiles</c> instead costs a <c>stat</c> per <c>FileInfo</c> plus a
    /// second pass for subdirectories: on a cold rotational drive that measured 4.0s against 0.35s.
    /// </remarks>
    private static List<string> CollectSampleFiles(IReadOnlyList<string> searchRoots, CancellationToken ct)
    {
        var files = new List<string>(TargetSampleCount);
        var queue = new Queue<string>();
        foreach (var root in searchRoots)
        {
            if (!string.IsNullOrWhiteSpace(root)) queue.Enqueue(root);
        }

        var options = new EnumerationOptions { IgnoreInaccessible = true };
        int directoriesVisited = 0;

        while (queue.Count > 0 && files.Count < TargetSampleCount && directoriesVisited < MaxDirectoriesVisited)
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
                        queue.Enqueue(entry.Path);
                        continue;
                    }

                    if (takenHere >= MaxFilesPerDirectory || entry.Length < SampleBlockBytes) continue;

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

                DisableFileCache(handle);

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
                    ClassifyLatencies(latencies) != DriveMediaType.Unknown)
                    break;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Locked or vanished file: skip it.
            }
        }

        return latencies;
    }

    private const int F_NOCACHE = 48;

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int fd, int cmd, int arg);

    /// <summary>
    /// Bypasses the unified buffer cache so a sample measures the device, not RAM. Best-effort: if it
    /// fails the reads still run, they just risk reading cached data and looking artificially fast.
    /// </summary>
    private static void DisableFileCache(SafeFileHandle handle)
    {
        if (!OperatingSystem.IsMacOS()) return;

        try
        {
            fcntl((int)handle.DangerousGetHandle(), F_NOCACHE, 1);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }
}
