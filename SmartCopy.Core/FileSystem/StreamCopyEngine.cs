namespace SmartCopy.Core.FileSystem;

/// <summary>
/// Provider-agnostic stream pump: copies bytes from a source stream to an already-opened destination
/// stream, honouring the buffer size, write mode, preallocation, ArrayPool, and progress settings in
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
    /// Copies <paramref name="source"/> into <paramref name="destination"/> and flushes. When the
    /// length is known it is passed as <paramref name="remainingBytes"/> so the write mode and
    /// preallocation heuristics can apply; pass null for unknown-length streams.
    /// </summary>
    public static async Task CopyAsync(
        Stream source,
        Stream destination,
        long? remainingBytes,
        IProgress<long>? progress,
        OperationalSettings opts,
        CancellationToken ct)
    {
        var writeMode = DetermineWriteMode(remainingBytes, opts);

        if (writeMode == LocalFileSystemWriteMode.CopyToAsync)
        {
            // CopyToAsync mode: wraps the source in a ProgressReportingReadStream so the
            // framework's internal buffer loop reports progress without a manual loop here.
            await CopyViaCopyToAsync(source, destination, progress, opts, ct);
        }
        else if (writeMode == LocalFileSystemWriteMode.Auto &&
                 remainingBytes is long autoBytesRemaining &&
                 autoBytesRemaining <= opts.SmallFileProgressThresholdBytes)
        {
            // Small-file optimisation: for files whose full size fits in memory we know
            // exactly how many bytes will be written, so we let the framework copy without
            // overhead and report progress in one shot at the end instead of per-chunk.
            await source.CopyToAsync(destination, opts.CopyBufferSizeBytes, ct);
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

        await destination.FlushAsync(ct);
    }

    private static LocalFileSystemWriteMode DetermineWriteMode(
        long? remainingBytes,
        OperationalSettings opts)
    {
        if (opts.WriteMode != LocalFileSystemWriteMode.Auto)
            return opts.WriteMode;
        // Unknown length: can't apply the small-file optimisation, go straight to manual loop.
        if (remainingBytes is null)
            return LocalFileSystemWriteMode.ManualLoop;
        // Known length: the size heuristic in CopyAsync resolves the path inline.
        return LocalFileSystemWriteMode.Auto;
    }

    private static async Task CopyViaCopyToAsync(
        Stream data,
        Stream output,
        IProgress<long>? progress,
        OperationalSettings opts,
        CancellationToken ct)
    {
        Stream source = data;
        if (progress is not null)
        {
            source = new ProgressReportingReadStream(data, progress);
        }

        await source.CopyToAsync(output, opts.CopyBufferSizeBytes, ct);
    }

    private static async Task CopyWithManualLoopAsync(
        Stream data,
        Stream output,
        IProgress<long>? progress,
        OperationalSettings opts,
        CancellationToken ct)
    {
        if (opts.UseArrayPoolForManualLoop)
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

            return;
        }

        var buffer = new byte[opts.CopyBufferSizeBytes];
        await CopyWithManualLoopCoreAsync(data, output, buffer, progress, ct);
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
