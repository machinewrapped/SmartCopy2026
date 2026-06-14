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

        await buf.AccumulateAsync(new MemoryStream(bytes), bytes.Length, "/dest/file.txt",
            DestinationResult.Created, node, CancellationToken.None);

        Assert.Single(buf.Entries);
        Assert.Equal(5, buf.Entries[0].Length);
        Assert.Equal(0, buf.Entries[0].Offset);
        Assert.Equal("/dest/file.txt", buf.Entries[0].Destination);
        Assert.Equal(DestinationResult.Created, buf.Entries[0].DestResult);
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
            MakeNode("a", a.Length), CancellationToken.None);
        await buf.AccumulateAsync(new MemoryStream(b), b.Length, "/d/b", DestinationResult.Created,
            MakeNode("b", b.Length), CancellationToken.None);

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
            MakeNode("x", 3), CancellationToken.None);

        Assert.True(buf.HasEntries);

        buf.Reset();

        Assert.False(buf.HasEntries);
        Assert.Empty(buf.Entries);
        Assert.True(buf.HasSpaceFor(64)); // position reset to 0
    }

    [Fact]
    public async Task OpenSegmentStream_ReturnsCorrectSlice()
    {
        using var buf = new BatchCopyBuffer(16);
        var data1 = new byte[] { 0xAA, 0xBB };
        var data2 = new byte[] { 0xCC, 0xDD, 0xEE };

        await buf.AccumulateAsync(new MemoryStream(data1), data1.Length, "/a", DestinationResult.Created,
            MakeNode("a", data1.Length), CancellationToken.None);
        await buf.AccumulateAsync(new MemoryStream(data2), data2.Length, "/b", DestinationResult.Created,
            MakeNode("b", data2.Length), CancellationToken.None);

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
}
