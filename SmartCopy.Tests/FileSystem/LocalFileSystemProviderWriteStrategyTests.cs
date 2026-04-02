using System.Text;
using SmartCopy.Core.FileSystem;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.FileSystem;

public sealed class LocalFileSystemProviderWriteStrategyTests
{
    [Fact]
    public async Task WriteAsync_CopyToAsyncMode_ReportsProgressAndWritesContent()
    {
        using var temp = new TempDirectory();
        var provider = new LocalFileSystemProvider(
            temp.Path,
            options: new LocalFileSystemProviderOptions
            {
                CopyBufferSizeBytes = 4,
                WriteMode = LocalFileSystemWriteMode.CopyToAsync,
            });

        var destination = Path.Combine(temp.Path, "copytoasync.txt");
        var payload = Encoding.UTF8.GetBytes("progress-check");
        var reportedBytes = 0L;
        IProgress<long> progress = new SyncProgress<long>(bytes => reportedBytes += bytes);

        await using var source = new MemoryStream(payload);
        await provider.WriteAsync(destination, source, progress, CancellationToken.None);

        Assert.Equal(payload.LongLength, reportedBytes);
        Assert.Equal("progress-check", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task WriteAsync_ManualLoopWithArrayPool_WritesContentAndReportsProgress()
    {
        using var temp = new TempDirectory();
        var provider = new LocalFileSystemProvider(
            temp.Path,
            options: new LocalFileSystemProviderOptions
            {
                CopyBufferSizeBytes = 3,
                WriteMode = LocalFileSystemWriteMode.ManualLoop,
                UseArrayPoolForManualLoop = true,
                PreallocateDestinationFile = true,
            });

        var destination = Path.Combine(temp.Path, "manualloop.txt");
        var payload = Encoding.UTF8.GetBytes("arraypool-check");
        var reportedBytes = 0L;
        IProgress<long> progress = new SyncProgress<long>(bytes => reportedBytes += bytes);

        await using var source = new MemoryStream(payload);
        await provider.WriteAsync(destination, source, progress, CancellationToken.None);

        Assert.Equal(payload.LongLength, reportedBytes);
        Assert.Equal(payload.LongLength, new FileInfo(destination).Length);
        Assert.Equal("arraypool-check", await File.ReadAllTextAsync(destination));
    }

    [Fact]
    public async Task WriteAsync_WhenInterrupted_DoesNotLeaveDestinationFile()
    {
        using var temp = new TempDirectory();
        var provider = new LocalFileSystemProvider(temp.Path);
        var destination = Path.Combine(temp.Path, "interrupted-new.txt");

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await using var source = new InterruptingReadStream(totalBytes: 1024, throwAfterBytes: 256);
            await provider.WriteAsync(destination, source, progress: null, CancellationToken.None);
        });

        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.smartcopy.tmp.*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task WriteAsync_WhenInterrupted_LeavesExistingDestinationUntouched()
    {
        using var temp = new TempDirectory();
        var provider = new LocalFileSystemProvider(temp.Path);
        var destination = Path.Combine(temp.Path, "interrupted-overwrite.txt");
        await File.WriteAllTextAsync(destination, "original");

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await using var source = new InterruptingReadStream(totalBytes: 4096, throwAfterBytes: 512);
            await provider.WriteAsync(destination, source, progress: null, CancellationToken.None);
        });

        Assert.Equal("original", await File.ReadAllTextAsync(destination));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.smartcopy.tmp.*", SearchOption.TopDirectoryOnly));
    }

    private sealed class InterruptingReadStream(int totalBytes, int throwAfterBytes) : Stream
    {
        private readonly int _totalBytes = totalBytes;
        private readonly int _throwAfterBytes = throwAfterBytes;
        private int _position;
        private bool _thrown;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _totalBytes;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("Sync reads are not used by this test stream.");

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_position >= _totalBytes)
            {
                return ValueTask.FromResult(0);
            }

            if (!_thrown && _position >= _throwAfterBytes)
            {
                _thrown = true;
                throw new OperationCanceledException("Simulated transfer interruption.");
            }

            var remaining = _totalBytes - _position;
            var nextChunk = Math.Min(Math.Min(buffer.Length, remaining), 256);
            buffer.Slice(0, nextChunk).Span.Fill((byte)'x');
            _position += nextChunk;
            return ValueTask.FromResult(nextChunk);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }
}
