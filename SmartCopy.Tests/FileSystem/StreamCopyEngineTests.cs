using SmartCopy.Core.FileSystem;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.FileSystem;

public sealed class StreamCopyEngineTests
{
    [Fact]
    public async Task CopyAsync_AutoSmallFileWithArrayPool_UsesManualLoopAndReportsCompletionOnce()
    {
        var payload = "auto-small-pooled"u8.ToArray();
        var reported = new List<long>();
        IProgress<long> progress = new SyncProgress<long>(reported.Add);

        var settings = new OperationalSettings
        {
            CopyBufferSizeBytes = 4,
            SmallFileProgressThresholdBytes = payload.LongLength,
            WriteMode = LocalFileSystemWriteMode.Auto,
            UseArrayPoolForManualLoop = true,
        };

        await using var source = new CopyToAsyncFailingMemoryStream(payload);
        await using var destination = new MemoryStream();

        await StreamCopyEngine.CopyAsync(
            source,
            destination,
            payload.LongLength,
            progress,
            settings,
            CancellationToken.None);

        Assert.False(source.CopyToAsyncCalled);
        Assert.Collection(reported, bytes => Assert.Equal(payload.LongLength, bytes));
        Assert.Equal(payload, destination.ToArray());
    }

    private sealed class CopyToAsyncFailingMemoryStream(byte[] payload) : MemoryStream(payload, writable: false)
    {
        public bool CopyToAsyncCalled { get; private set; }

        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            CopyToAsyncCalled = true;
            throw new InvalidOperationException(
                "Auto small-file path should not call CopyToAsync when ArrayPool is enabled.");
        }
    }
}
