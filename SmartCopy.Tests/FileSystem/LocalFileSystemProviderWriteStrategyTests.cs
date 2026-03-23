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
        var progress = new Progress<long>(bytes => reportedBytes += bytes);

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
        var progress = new Progress<long>(bytes => reportedBytes += bytes);

        await using var source = new MemoryStream(payload);
        await provider.WriteAsync(destination, source, progress, CancellationToken.None);

        Assert.Equal(payload.LongLength, reportedBytes);
        Assert.Equal(payload.LongLength, new FileInfo(destination).Length);
        Assert.Equal("arraypool-check", await File.ReadAllTextAsync(destination));
    }
}
