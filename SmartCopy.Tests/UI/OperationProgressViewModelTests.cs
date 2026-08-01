using SmartCopy.Core.Progress;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.Tests.UI;

public sealed class OperationProgressViewModelTests
{
    [Theory]
    [InlineData(1023, 1, "1023B/s")]
    [InlineData(1024, 1, "1.0KB/s")]
    [InlineData(3 * 1024, 2, "1.5KB/s")]
    [InlineData(1024 * 1024, 1, "1.0MB/s")]
    [InlineData(3 * 1024 * 1024, 2, "1.5MB/s")]
    [InlineData(1024L * 1024 * 1024, 1, "1.00GB/s")]
    public void Update_SetsRollingTransferSpeed(long completedBytes, int elapsedSeconds, string expected)
    {
        var vm = new OperationProgressViewModel();

        Report(vm, completedBytes: 0, elapsedSeconds: 0);
        Report(vm, completedBytes, elapsedSeconds);

        Assert.Equal(expected, vm.TransferSpeed);
    }

    [Fact]
    public void Update_DropsSamplesOlderThanTenSeconds()
    {
        var vm = new OperationProgressViewModel();

        Report(vm, completedBytes: 0, elapsedSeconds: 1);
        Report(vm, completedBytes: 10 * 1024 * 1024, elapsedSeconds: 2);
        Assert.Equal("10.0MB/s", vm.TransferSpeed);

        Report(vm, completedBytes: 11 * 1024 * 1024, elapsedSeconds: 12);

        Assert.Equal("102.4KB/s", vm.TransferSpeed);
    }

    [Fact]
    public void Update_WithNoMeasuredTransfer_ClearsTransferSpeed()
    {
        var vm = new OperationProgressViewModel();

        Report(vm, completedBytes: 0, elapsedSeconds: 1);
        Report(vm, completedBytes: 1024 * 1024, elapsedSeconds: 2);
        Assert.Equal("1.0MB/s", vm.TransferSpeed);

        Report(vm, completedBytes: 1024 * 1024, elapsedSeconds: 12);

        Assert.Equal("0 B/s", vm.TransferSpeed);
    }

    private static void Report(OperationProgressViewModel vm, long completedBytes, int elapsedSeconds)
    {
        vm.Update(new OperationProgress(
            CurrentFile: "video.mp4",
            CurrentFileBytes: completedBytes,
            CurrentFileTotalBytes: completedBytes,
            FilesCompleted: 0,
            FilesTotal: 1,
            TotalBytesCompleted: completedBytes,
            TotalBytes: completedBytes * 2,
            Elapsed: TimeSpan.FromSeconds(elapsedSeconds),
            EstimatedRemaining: TimeSpan.FromSeconds(2)));
    }
}
