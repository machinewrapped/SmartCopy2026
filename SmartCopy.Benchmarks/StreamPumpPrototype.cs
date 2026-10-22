using System.Buffers;
using Microsoft.Win32.SafeHandles;

namespace SmartCopy.Benchmarks;

/// <summary>
/// Phase 7 inter-phase parallelism prototype: how a single large file's bytes are pumped from source
/// to destination. Benchmark-only; production uses the strictly serial <c>StreamCopyEngine</c> loop.
/// </summary>
internal enum StreamPumpMode
{
    /// <summary>Await read, await write, repeat — the production <c>StreamCopyEngine</c> loop.</summary>
    Serial,

    /// <summary>Double-buffered: the read of chunk N+1 is in flight while chunk N is written.
    /// Two buffers, at most one read and one write outstanding.</summary>
    Overlapped,

    /// <summary>As <see cref="Overlapped"/> with up to two reads in flight (three buffers).</summary>
    Overlapped2Reads,
}

/// <summary>
/// Pumps one file between two <see cref="SafeFileHandle"/>s using positional
/// <see cref="RandomAccess"/> I/O. An unbuffered asynchronous <see cref="FileStream"/> (the production
/// configuration) issues exactly these calls, so <see cref="StreamPumpMode.Serial"/> is the production
/// loop and the overlapped modes differ from it only in how many operations are outstanding.
/// Self-contained (no SmartCopy.Core dependency) so it can be compiled into a stand-alone check.
/// </summary>
internal static class StreamPumpPrototype
{
    /// <summary>Copies the whole of <paramref name="source"/> to <paramref name="destination"/> and
    /// returns the number of bytes written.</summary>
    public static Task<long> CopyAsync(
        SafeFileHandle source,
        SafeFileHandle destination,
        int bufferSize,
        StreamPumpMode mode,
        CancellationToken ct) => mode switch
        {
            StreamPumpMode.Serial => CopySerialAsync(source, destination, bufferSize, ct),
            StreamPumpMode.Overlapped => CopyOverlappedAsync(source, destination, bufferSize, readsInFlight: 1, ct),
            StreamPumpMode.Overlapped2Reads => CopyOverlappedAsync(source, destination, bufferSize, readsInFlight: 2, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };

    private static async Task<long> CopySerialAsync(
        SafeFileHandle source,
        SafeFileHandle destination,
        int bufferSize,
        CancellationToken ct)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        try
        {
            long offset = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var read = await RandomAccess.ReadAsync(source, buffer.AsMemory(0, bufferSize), offset, ct);
                if (read == 0)
                    return offset;

                await RandomAccess.WriteAsync(destination, buffer.AsMemory(0, read), offset, ct);
                offset += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private readonly record struct PendingRead(Task<int> Task, int Buffer, long Offset);

    private static async Task<long> CopyOverlappedAsync(
        SafeFileHandle source,
        SafeFileHandle destination,
        int bufferSize,
        int readsInFlight,
        CancellationToken ct)
    {
        // One buffer per outstanding read plus the one being written.
        var buffers = new byte[readsInFlight + 1][];
        var free = new Stack<int>(buffers.Length);
        for (var i = 0; i < buffers.Length; i++)
        {
            buffers[i] = ArrayPool<byte>.Shared.Rent(bufferSize);
            free.Push(i);
        }

        var reads = new Queue<PendingRead>(readsInFlight);
        Task write = Task.CompletedTask;
        var writeBuffer = -1;
        long nextReadOffset = 0;
        long written = 0;
        var endOfFile = false;

        void IssueReads()
        {
            while (!endOfFile && reads.Count < readsInFlight && free.Count > 0)
            {
                var b = free.Pop();
                var task = RandomAccess.ReadAsync(source, buffers[b].AsMemory(0, bufferSize), nextReadOffset, ct).AsTask();
                reads.Enqueue(new PendingRead(task, b, nextReadOffset));
                nextReadOffset += bufferSize;
            }
        }

        try
        {
            IssueReads();
            while (reads.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var chunk = reads.Dequeue();
                var read = await chunk.Task;

                if (read == 0)
                {
                    endOfFile = true;
                    free.Push(chunk.Buffer);
                    continue;
                }

                // Chunks are bufferSize-aligned, so only the final chunk may be short. Data after a
                // short read would leave a hole at the destination.
                if (endOfFile)
                    throw new IOException($"Unexpected short read before offset {chunk.Offset}.");
                if (read < bufferSize)
                    endOfFile = true;

                // At most one write outstanding; its completion frees that buffer for the next read.
                await write;
                if (writeBuffer >= 0)
                {
                    free.Push(writeBuffer);
                    writeBuffer = -1;
                }

                // Issue the read-ahead before the write: NTFS completes file-extending writes
                // synchronously even on an overlapped handle, so a read issued afterwards would not
                // overlap the write at all.
                IssueReads();
                write = RandomAccess.WriteAsync(destination, buffers[chunk.Buffer].AsMemory(0, read), chunk.Offset, ct).AsTask();
                writeBuffer = chunk.Buffer;
                written += read;
            }

            await write;
            return written;
        }
        finally
        {
            // Never return a buffer to the pool while the OS may still be reading into or writing from it.
            foreach (var pending in reads)
                await ObserveAsync(pending.Task);
            await ObserveAsync(write);

            foreach (var buffer in buffers)
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task;
        }
        catch
        {
            // The primary exception is already propagating; this only drains outstanding I/O.
        }
    }
}
