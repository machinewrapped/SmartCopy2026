namespace SmartCopy.Core.FileSystem;

/// <summary>
/// Provider-agnostic stream pump: copies bytes from a source stream to an already-opened destination
/// stream, honouring the buffer size and progress settings in
/// <see cref="OperationalSettings"/>.
/// <para>
/// This is the generic half of a file write. Providers keep ownership of the provider-specific half —
/// how the destination stream is created and committed (staged temp-file + atomic rename for the local
/// filesystem, an in-memory buffer for the memory provider, a protocol session for MTP) — and call into
/// this engine for the actual transfer so the copy mechanics are not re-implemented per provider.
/// </para>
/// </summary>
internal static class StreamCopyEngine
{
    /// <summary>
    /// Returns the number of bytes remaining in <paramref name="data"/> when the length is known
    /// (seekable streams), so callers can make size-dependent decisions before pumping.
    /// </summary>
    public static bool TryGetRemainingLength(Stream data, out long remainingBytes)
    {
        if (!data.CanSeek)
        {
            remainingBytes = 0;
            return false;
        }

        remainingBytes = Math.Max(0, data.Length - data.Position);
        return true;
    }

    /// <summary>
    /// Copies <paramref name="source"/> into <paramref name="destination"/>. When the length is known
    /// it is passed as <paramref name="remainingBytes"/> so small files can report completion once;
    /// pass null for unknown-length streams. The caller owns stream disposal, which flushes managed buffers.
    /// </summary>
    public static async Task CopyAsync(
        Stream source,
        Stream destination,
        long? remainingBytes,
        IProgress<long>? progress,
        OperationalSettings opts,
        CancellationToken ct)
    {
        if (remainingBytes is long autoBytesRemaining &&
            autoBytesRemaining <= opts.SmallFileProgressThresholdBytes)
        {
            // Small-file optimisation: for files whose full size fits in memory we know
            // exactly how many bytes will be written, so report progress in one shot at the end
            // instead of per-chunk.
            await CopyWithManualLoopAsync(source, destination, progress: null, opts, ct);

            if (progress is not null && autoBytesRemaining > 0)
            {
                progress.Report(autoBytesRemaining);
            }
        }
        else
        {
            // Large files and unknown-length streams: manual loop reports progress per chunk,
            // which keeps UI responsive during long transfers without adding a stream wrapper.
            await CopyWithManualLoopAsync(source, destination, progress, opts, ct);
        }
    }

    private static async Task CopyWithManualLoopAsync(
        Stream data,
        Stream output,
        IProgress<long>? progress,
        OperationalSettings opts,
        CancellationToken ct)
    {
        var rentedBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(opts.CopyBufferSizeBytes);
        try
        {
            await CopyWithManualLoopCoreAsync(data, output, rentedBuffer, progress, ct);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    private static async Task CopyWithManualLoopCoreAsync(
        Stream data,
        Stream output,
        byte[] buffer,
        IProgress<long>? progress,
        CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await data.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            progress?.Report(read);
        }
    }
}
