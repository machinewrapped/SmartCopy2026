using System.Buffers;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.Tests.Pipeline;

public sealed class BatchCopyBufferTests
{
    [Fact]
    public void HasSpaceFor_ReflectsUsedBytes()
    {
        using var buf = new BatchCopyBuffer(100);
        Assert.True(buf.HasSpaceFor(100));
        Assert.True(buf.HasSpaceFor(50));
        Assert.False(buf.HasSpaceFor(101));
    }

    [Fact]
    public async Task AccumulateAsync_StoresDataAndEntry()
    {
        using var buf = new BatchCopyBuffer(256);
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var node = MakeNode("file.txt", bytes.Length);

        var preWrite = TimeSpan.FromMilliseconds(7);
        using var source = new DelayedReadStream(bytes, TimeSpan.FromMilliseconds(20));
        var readStart = System.Diagnostics.Stopwatch.GetTimestamp();
        await buf.AccumulateAsync(source, bytes.Length, "/dest/file.txt",
            DestinationResult.Created, node, preWrite, readStart, CancellationToken.None);

        Assert.Single(buf.Entries);
        Assert.Equal(5, buf.Entries[0].Length);
        Assert.Equal(0, buf.Entries[0].Offset);
        Assert.Equal("/dest/file.txt", buf.Entries[0].Destination);
        Assert.Equal(DestinationResult.Created, buf.Entries[0].DestResult);
        Assert.True(buf.Entries[0].PreWriteElapsed >= preWrite + TimeSpan.FromMilliseconds(15));
        Assert.Same(node, buf.Entries[0].Node);

        // Verify data round-trip
        using var ms = buf.OpenSegmentStream(buf.Entries[0]);
        Assert.Equal(bytes, ms.ToArray());
    }

    [Fact]
    public async Task AccumulateAsync_MultipleFiles_PackedConsecutively()
    {
        using var buf = new BatchCopyBuffer(64);
        var a = new byte[] { 10, 20 };
        var b = new byte[] { 30, 40, 50 };

        await buf.AccumulateAsync(new MemoryStream(a), a.Length, "/d/a", DestinationResult.Created,
            MakeNode("a", a.Length), TimeSpan.FromMilliseconds(1),
            System.Diagnostics.Stopwatch.GetTimestamp(), CancellationToken.None);
        await buf.AccumulateAsync(new MemoryStream(b), b.Length, "/d/b", DestinationResult.Created,
            MakeNode("b", b.Length), TimeSpan.FromMilliseconds(2),
            System.Diagnostics.Stopwatch.GetTimestamp(), CancellationToken.None);

        Assert.Equal(2, buf.Entries.Count);
        Assert.Equal(0, buf.Entries[0].Offset);
        Assert.Equal(2, buf.Entries[0].Length);
        Assert.Equal(2, buf.Entries[1].Offset);
        Assert.Equal(3, buf.Entries[1].Length);

        Assert.False(buf.HasSpaceFor(64 - 5 + 1)); // 59 bytes left, 60 won't fit
        Assert.True(buf.HasSpaceFor(64 - 5));       // exactly 59 bytes fits
    }

    [Fact]
    public async Task Reset_ClearsEntriesAndPosition()
    {
        using var buf = new BatchCopyBuffer(64);
        await buf.AccumulateAsync(new MemoryStream([1, 2, 3]), 3, "/d/x", DestinationResult.Created,
            MakeNode("x", 3), TimeSpan.FromMilliseconds(1),
            System.Diagnostics.Stopwatch.GetTimestamp(), CancellationToken.None);

        Assert.True(buf.HasEntries);

        buf.Reset();

        Assert.False(buf.HasEntries);
        Assert.Empty(buf.Entries);
        Assert.True(buf.HasSpaceFor(64)); // position reset to 0
    }

    [Fact]
    public async Task Reset_ClearsPooledEntryReferences()
    {
        var entryPool = new TrackingEntryPool();
        using var buf = new BatchCopyBuffer(
            64,
            ArrayPool<byte>.Shared,
            entryPool,
            initialEntryCapacity: 4);

        await buf.AccumulateAsync(new MemoryStream([1, 2, 3]), 3, "/d/x", DestinationResult.Created,
            MakeNode("x", 3), TimeSpan.FromMilliseconds(1),
            System.Diagnostics.Stopwatch.GetTimestamp(), CancellationToken.None);

        Assert.NotNull(entryPool.LastRented[0].Node);
        Assert.NotNull(entryPool.LastRented[0].Destination);

        buf.Reset();

        Assert.Null(entryPool.LastRented[0].Node);
        Assert.Null(entryPool.LastRented[0].Destination);
    }

    [Fact]
    public async Task HasCapacityFor_ReflectsEntryLimit()
    {
        using var buf = new BatchCopyBuffer(
            64,
            ArrayPool<byte>.Shared,
            ArrayPool<BatchCopyBuffer.Entry>.Shared,
            initialEntryCapacity: 2,
            maxEntriesPerFlush: 2);

        Assert.True(buf.HasCapacityFor(0));

        await buf.AccumulateAsync(new MemoryStream([]), 0, "/d/a", DestinationResult.Created,
            MakeNode("a", 0), TimeSpan.FromMilliseconds(1),
            System.Diagnostics.Stopwatch.GetTimestamp(), CancellationToken.None);
        await buf.AccumulateAsync(new MemoryStream([]), 0, "/d/b", DestinationResult.Created,
            MakeNode("b", 0), TimeSpan.FromMilliseconds(2),
            System.Diagnostics.Stopwatch.GetTimestamp(), CancellationToken.None);

        Assert.True(buf.HasSpaceFor(0));
        Assert.False(buf.HasCapacityFor(0));

        buf.Reset();

        Assert.True(buf.HasCapacityFor(0));
    }

    [Fact]
    public async Task Entries_AreReturnedToPoolOnGrowthAndDispose()
    {
        var entryPool = new TrackingEntryPool();
        var buf = new BatchCopyBuffer(
            64,
            ArrayPool<byte>.Shared,
            entryPool,
            initialEntryCapacity: 1);

        await buf.AccumulateAsync(new MemoryStream([1]), 1, "/d/a", DestinationResult.Created,
            MakeNode("a", 1), TimeSpan.FromMilliseconds(1),
            System.Diagnostics.Stopwatch.GetTimestamp(), CancellationToken.None);
        await buf.AccumulateAsync(new MemoryStream([2]), 1, "/d/b", DestinationResult.Created,
            MakeNode("b", 1), TimeSpan.FromMilliseconds(2),
            System.Diagnostics.Stopwatch.GetTimestamp(), CancellationToken.None);

        Assert.Equal(2, entryPool.RentCount);
        Assert.Single(entryPool.ReturnClearFlags);
        Assert.True(entryPool.ReturnClearFlags[0]);

        buf.Dispose();

        Assert.Equal(2, entryPool.ReturnCount);
        Assert.All(entryPool.ReturnClearFlags, Assert.True);
    }

    [Fact]
    public async Task OpenSegmentStream_ReturnsCorrectSlice()
    {
        using var buf = new BatchCopyBuffer(16);
        var data1 = new byte[] { 0xAA, 0xBB };
        var data2 = new byte[] { 0xCC, 0xDD, 0xEE };

        await buf.AccumulateAsync(new MemoryStream(data1), data1.Length, "/a", DestinationResult.Created,
            MakeNode("a", data1.Length), TimeSpan.FromMilliseconds(1),
            System.Diagnostics.Stopwatch.GetTimestamp(), CancellationToken.None);
        await buf.AccumulateAsync(new MemoryStream(data2), data2.Length, "/b", DestinationResult.Created,
            MakeNode("b", data2.Length), TimeSpan.FromMilliseconds(2),
            System.Diagnostics.Stopwatch.GetTimestamp(), CancellationToken.None);

        using var s1 = buf.OpenSegmentStream(buf.Entries[0]);
        using var s2 = buf.OpenSegmentStream(buf.Entries[1]);

        Assert.Equal(data1, s1.ToArray());
        Assert.Equal(data2, s2.ToArray());
    }

    private static FileNode MakeNode(string name, long size) =>
        new(new FileSystemNode
        {
            Name = name,
            FullPath = "/" + name,
            IsDirectory = false,
            Size = size,
        }, parent: null);

    private sealed class DelayedReadStream(byte[] buffer, TimeSpan delay) : MemoryStream(buffer)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return await base.ReadAsync(destination, cancellationToken);
        }
    }

    private sealed class TrackingEntryPool : ArrayPool<BatchCopyBuffer.Entry>
    {
        public int RentCount { get; private set; }
        public int ReturnCount { get; private set; }
        public BatchCopyBuffer.Entry[] LastRented { get; private set; } = [];
        public List<bool> ReturnClearFlags { get; } = [];

        public override BatchCopyBuffer.Entry[] Rent(int minimumLength)
        {
            RentCount++;
            LastRented = new BatchCopyBuffer.Entry[Math.Max(1, minimumLength)];
            return LastRented;
        }

        public override void Return(BatchCopyBuffer.Entry[] array, bool clearArray = false)
        {
            ReturnCount++;
            ReturnClearFlags.Add(clearArray);
            if (clearArray)
                Array.Clear(array);
        }
    }
}
