using System.Diagnostics;
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
    public void Update_WithNoMeasuredTransfer_ReportsZero()
    {
        var vm = new OperationProgressViewModel();

        Report(vm, completedBytes: 0, elapsedSeconds: 1);
        Report(vm, completedBytes: 1024 * 1024, elapsedSeconds: 2);
        Assert.Equal("1.0MB/s", vm.TransferSpeed);

        Report(vm, completedBytes: 1024 * 1024, elapsedSeconds: 12);

        Assert.Equal("0B/s", vm.TransferSpeed);
    }

    [Fact]
    public void TickStaleness_WhenReportsStop_DecaysRateTowardsZero()
    {
        var clock = new FakeClock();
        var vm = ActiveViewModel(clock);

        Report(vm, completedBytes: 0, elapsedSeconds: 0);
        Report(vm, completedBytes: 10 * 1024 * 1024, elapsedSeconds: 1);
        Assert.Equal("10.0MB/s", vm.TransferSpeed);

        // The 10MB measured over 1s of reports now spans 4s of wall clock.
        clock.Advance(TimeSpan.FromSeconds(3));
        vm.TickStaleness();
        Assert.Equal("2.5MB/s", vm.TransferSpeed);

        // Every sample has now aged out of the 10s window.
        clock.Advance(TimeSpan.FromSeconds(30));
        vm.TickStaleness();
        Assert.Equal("0B/s", vm.TransferSpeed);
    }

    [Fact]
    public void TickStaleness_WhenStepDoesNotTransferData_LeavesSpeedBlank()
    {
        var clock = new FakeClock();
        var vm = ActiveViewModel(clock);

        Report(vm, completedBytes: 0, elapsedSeconds: 0, transfersData: false);
        Report(vm, completedBytes: 10 * 1024 * 1024, elapsedSeconds: 1, transfersData: false);
        clock.Advance(TimeSpan.FromSeconds(3));
        vm.TickStaleness();

        Assert.Equal(string.Empty, vm.TransferSpeed);
    }

    [Fact]
    public void Update_WhenStepStopsTransferringData_ClearsSpeed()
    {
        var vm = new OperationProgressViewModel();

        Report(vm, completedBytes: 0, elapsedSeconds: 0);
        Report(vm, completedBytes: 10 * 1024 * 1024, elapsedSeconds: 1);
        Assert.Equal("10.0MB/s", vm.TransferSpeed);

        // A following delete step reports no transfer.
        Report(vm, completedBytes: 1024, elapsedSeconds: 0, transfersData: false);

        Assert.Equal(string.Empty, vm.TransferSpeed);
    }

    [Fact]
    public void Update_WhenNextStepRestartsTheClock_StartsANewWindow()
    {
        var vm = new OperationProgressViewModel();

        Report(vm, completedBytes: 0, elapsedSeconds: 0);
        Report(vm, completedBytes: 10 * 1024 * 1024, elapsedSeconds: 5);
        Assert.Equal("2.0MB/s", vm.TransferSpeed);

        // A second executable step restarts the reporter's stopwatch and zeroes its byte count.
        // Without a reset the window would span the discontinuity and report nonsense.
        Report(vm, completedBytes: 0, elapsedSeconds: 0);
        Report(vm, completedBytes: 4 * 1024 * 1024, elapsedSeconds: 1);

        Assert.Equal("4.0MB/s", vm.TransferSpeed);
    }

    private static OperationProgressViewModel ActiveViewModel(FakeClock clock)
    {
        // TickStaleness only runs for a live operation. Begin() needs a real PipelineJob, so set
        // the flag directly rather than standing up a whole pipeline.
        return new OperationProgressViewModel(clock) { IsActive = true };
    }

    private static void Report(
        OperationProgressViewModel vm,
        long completedBytes,
        int elapsedSeconds,
        bool transfersData = true)
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
            EstimatedRemaining: TimeSpan.FromSeconds(2),
            TransfersData: transfersData));
    }

    /// <summary>Manually advanced clock; only <see cref="GetTimestamp"/> needs overriding.</summary>
    private sealed class FakeClock : TimeProvider
    {
        private long _timestamp = Stopwatch.GetTimestamp();

        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan by) => _timestamp += (long)(by.TotalSeconds * TimestampFrequency);
    }
}
