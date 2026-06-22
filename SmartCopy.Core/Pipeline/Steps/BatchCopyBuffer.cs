using System.Buffers;
using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.Core.Pipeline.Steps;

/// <summary>
/// Accumulates small-file reads into a single pool-rented buffer to enable phase-separated
/// I/O: all reads happen first, then all writes, avoiding read-write interleaving per file.
/// Dispose returns the buffer to <see cref="ArrayPool{T}.Shared"/>.
/// </summary>
internal sealed class BatchCopyBuffer : IDisposable
{
    internal readonly record struct Entry(
        DirectoryTreeNode Node,
        string Destination,
        DestinationResult DestResult,
        int Offset,
        int Length);

    private readonly byte[] _data;
    private readonly List<Entry> _entries = new();
    private int _used;
    private TimeSpan _preWriteElapsed;

    public long Capacity { get; }
    public bool HasEntries => _entries.Count > 0;
    public IReadOnlyList<Entry> Entries => _entries;

    /// <summary>
    /// Wall time spent on this batch's files before the write phase — each file's destination check plus
    /// its read into the buffer. The caller banks it via <see cref="AddPreWriteElapsed"/>; the strategy
    /// adds the flush time and splits the total evenly across the batch's files (per-file copy time in
    /// this regime is overhead-dominated, not byte-proportional). Cleared by <see cref="Reset"/>.
    /// </summary>
    public TimeSpan PreWriteElapsed => _preWriteElapsed;

    public BatchCopyBuffer(long capacityBytes)
    {
        Capacity = capacityBytes;
        _data = ArrayPool<byte>.Shared.Rent((int)capacityBytes);
    }

    /// <summary>Returns true if <paramref name="fileSize"/> bytes fit in the remaining space.</summary>
    public bool HasSpaceFor(int fileSize) => _used + fileSize <= (int)Capacity;

    /// <summary>
    /// Reads exactly <paramref name="fileSize"/> bytes from <paramref name="source"/> into
    /// the buffer and records the entry. Caller must ensure <see cref="HasSpaceFor"/> is true.
    /// </summary>
    public async Task AccumulateAsync(
        Stream source,
        int fileSize,
        string destination,
        DestinationResult destResult,
        DirectoryTreeNode node,
        CancellationToken ct)
    {
        await source.ReadExactlyAsync(_data.AsMemory(_used, fileSize), ct);
        _entries.Add(new Entry(node, destination, destResult, _used, fileSize));
        _used += fileSize;
    }

    /// <summary>Banks a file's pre-write time (destination check + read) for even attribution at flush.</summary>
    public void AddPreWriteElapsed(TimeSpan elapsed) => _preWriteElapsed += elapsed;

    /// <summary>Opens a read-only <see cref="MemoryStream"/> view over the entry's region.</summary>
    public MemoryStream OpenSegmentStream(Entry entry) =>
        new MemoryStream(_data, entry.Offset, entry.Length, writable: false);

    public void Reset()
    {
        _entries.Clear();
        _used = 0;
        _preWriteElapsed = TimeSpan.Zero;
    }

    public void Dispose() => ArrayPool<byte>.Shared.Return(_data);
}
