using System.Buffers;
using System.Diagnostics;
using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.Core.Pipeline.Steps;

/// <summary>
/// Accumulates small-file reads into a single pool-rented buffer to enable phase-separated
/// I/O: all reads happen first, then all writes, avoiding read-write interleaving per file.
/// Dispose returns the buffer to <see cref="ArrayPool{T}.Shared"/>.
/// </summary>
internal sealed partial class BatchCopyBuffer : IDisposable
{
    private const int DefaultInitialEntryCapacity = 1024;

    internal readonly record struct Entry(
        DirectoryTreeNode Node,
        string Destination,
        DestinationResult DestResult,
        int Offset,
        int Length,
        TimeSpan PreWriteElapsed);

    private readonly ArrayPool<byte> _dataPool;
    private readonly byte[] _data;
    private readonly PooledEntryBuffer _entries;
    private readonly int _maxEntriesPerFlush;
    private int _used;
    private bool _disposed;

    public long Capacity { get; }
    public bool HasEntries => _entries.Count > 0;
    public IReadOnlyList<Entry> Entries => _entries;

    public BatchCopyBuffer(long capacityBytes)
        : this(
            capacityBytes,
            ArrayPool<byte>.Shared,
            ArrayPool<Entry>.Shared,
            initialEntryCapacity: DefaultInitialEntryCapacity,
            maxEntriesPerFlush: DefaultInitialEntryCapacity)
    {
    }

    internal BatchCopyBuffer(
        long capacityBytes,
        ArrayPool<byte> dataPool,
        ArrayPool<Entry> entryPool,
        int initialEntryCapacity,
        int? maxEntriesPerFlush = null)
    {
        if (capacityBytes <= 0 || capacityBytes > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacityBytes),
                "Batch buffer size must be between 1 and Int32.MaxValue bytes.");
        }

        var maxEntries = maxEntriesPerFlush ?? initialEntryCapacity;
        if (maxEntries <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxEntriesPerFlush), "Max entries per flush must be positive.");

        Capacity = capacityBytes;
        _dataPool = dataPool;
        _data = _dataPool.Rent((int)capacityBytes);
        _entries = new PooledEntryBuffer(entryPool, initialEntryCapacity);
        _maxEntriesPerFlush = maxEntries;
    }

    /// <summary>Returns true if <paramref name="fileSize"/> bytes fit in the remaining space.</summary>
    public bool HasSpaceFor(int fileSize) => _used + fileSize <= (int)Capacity;

    /// <summary>
    /// Returns true when both the byte buffer and entry table can accept another file.
    /// The entry cap prevents huge batches of zero-byte or very tiny files from retaining
    /// unbounded destination strings before the next flush.
    /// </summary>
    public bool HasCapacityFor(int fileSize) => HasSpaceFor(fileSize) && _entries.Count < _maxEntriesPerFlush;

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
        TimeSpan preReadElapsed,
        long readStartTimestamp,
        CancellationToken ct)
    {
        await source.ReadExactlyAsync(_data.AsMemory(_used, fileSize), ct);
        var preWriteElapsed = preReadElapsed + Stopwatch.GetElapsedTime(readStartTimestamp);
        _entries.Add(new Entry(node, destination, destResult, _used, fileSize, preWriteElapsed));
        _used += fileSize;
    }

    /// <summary>Opens a read-only <see cref="MemoryStream"/> view over the entry's region.</summary>
    public MemoryStream OpenSegmentStream(Entry entry) =>
        new MemoryStream(_data, entry.Offset, entry.Length, writable: false);

    public void Reset()
    {
        _entries.Clear();
        _used = 0;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _entries.Dispose();
        _dataPool.Return(_data);
        _disposed = true;
    }

}
