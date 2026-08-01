using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Progress;

namespace SmartCopy.UI.ViewModels;

public partial class OperationProgressViewModel : ViewModelBase
{
    private static readonly TimeSpan TransferRateWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StalenessTickInterval = TimeSpan.FromSeconds(1);
    private static readonly string ZeroRate = FileSizeFormatter.FormatRate(0);

    private readonly TimeProvider _timeProvider;
    private readonly Queue<TransferRateSample> _transferRateSamples = new();

    private CancellationTokenSource? _cancellationTokenSource;
    private PauseTokenSource? _pauseTokenSource;
    private ITimer? _stalenessTimer;
    private SynchronizationContext? _syncContext;
    private TransferRateSample? _lastSample;
    private long _lastReportTimestamp;
    private bool _transfersData;

    public OperationProgressViewModel(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private double _percentComplete;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _currentFile = string.Empty;

    [ObservableProperty]
    private string _timeRemaining = string.Empty;

    [ObservableProperty]
    private string _transferSpeed = string.Empty;

    public PipelineJob Begin(PipelineJob job)
    {
        if (IsActive) throw new InvalidOperationException("Operation already in progress");

        _cancellationTokenSource = new CancellationTokenSource();
        _pauseTokenSource = new PauseTokenSource();
        IsActive = true;
        IsPaused = false;
        PercentComplete = 0;
        StatusText = "Starting operation...";
        CurrentFile = string.Empty;
        TimeRemaining = string.Empty;
        _transfersData = false;
        _lastReportTimestamp = _timeProvider.GetTimestamp();
        ResetTransferRate();
        StartStalenessTimer();

        return job with
        {
            CancellationToken = _cancellationTokenSource.Token,
            PauseToken = _pauseTokenSource,
            Progress = new Progress<OperationProgress>(Update)
        };
    }

    public void Complete()
    {
        _pauseTokenSource?.Resume();
        IsActive = false;
        IsPaused = false;
        StatusText = "Completed";
        TimeRemaining = "0:00 left";
        EndTransferRateTracking();

        _cancellationTokenSource?.Dispose();
        _pauseTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _pauseTokenSource = null;
    }

    public void Cancelled()
    {
        _pauseTokenSource?.Resume();
        IsActive = false;
        IsPaused = false;
        StatusText = "Cancelled";
        TimeRemaining = string.Empty;
        EndTransferRateTracking();

        _cancellationTokenSource?.Dispose();
        _pauseTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _pauseTokenSource = null;
    }

    public void Update(OperationProgress progress)
    {
        if (!IsPaused)
        {
            CurrentFile = progress.CurrentFile;
            StatusText = $"{progress.FilesCompleted}/{progress.FilesTotal} files";
        }
        PercentComplete = progress.TotalBytes <= 0
            ? 0
            : Math.Round((double)progress.TotalBytesCompleted / progress.TotalBytes * 100, 2);
        TimeRemaining = $"{progress.EstimatedRemaining:mm\\:ss} left";
        if (!IsPaused)
            UpdateTransferSpeed(progress);
    }

    /// <summary>
    /// Re-evaluates the rate against wall-clock time rather than against the elapsed time carried by
    /// progress reports, so that a transfer which stops reporting — a stalled network or MTP write,
    /// or a whole-file copy that never reports intermediate bytes — decays to zero instead of
    /// leaving the last measured rate on screen indefinitely.
    /// </summary>
    internal void TickStaleness()
    {
        if (!IsActive || IsPaused || !_transfersData || _lastSample is not { } last)
            return;

        RecomputeTransferSpeed(
            last.Elapsed + _timeProvider.GetElapsedTime(_lastReportTimestamp),
            last.TotalBytes);
    }

    private void UpdateTransferSpeed(OperationProgress progress)
    {
        if (_transfersData != progress.TransfersData)
        {
            _transfersData = progress.TransfersData;
            ResetTransferRate();
        }

        if (!_transfersData)
            return;

        // Each executable step restarts the reporter's stopwatch and zeroes its byte count, so a
        // multi-step pipeline drives both values backwards. Start a fresh window when that happens.
        if (_lastSample is { } latest &&
            (progress.Elapsed < latest.Elapsed || progress.TotalBytesCompleted < latest.TotalBytes))
        {
            _transferRateSamples.Clear();
        }

        var sample = new TransferRateSample(progress.Elapsed, progress.TotalBytesCompleted);
        _transferRateSamples.Enqueue(sample);
        _lastSample = sample;
        _lastReportTimestamp = _timeProvider.GetTimestamp();

        RecomputeTransferSpeed(progress.Elapsed, progress.TotalBytesCompleted);
    }

    private void RecomputeTransferSpeed(TimeSpan currentElapsed, long currentBytes)
    {
        while (_transferRateSamples.Count > 1 &&
               currentElapsed - _transferRateSamples.Peek().Elapsed > TransferRateWindow)
        {
            _transferRateSamples.Dequeue();
        }

        if (_transferRateSamples.Count == 0)
        {
            TransferSpeed = ZeroRate;
            return;
        }

        var first = _transferRateSamples.Peek();
        var span = currentElapsed - first.Elapsed;
        TransferSpeed = span > TimeSpan.Zero
            ? FileSizeFormatter.FormatRate((currentBytes - first.TotalBytes) / span.TotalSeconds)
            : ZeroRate;
    }

    /// <summary>Discards the sampling window. Shows zero while transferring, otherwise nothing.</summary>
    private void ResetTransferRate()
    {
        _transferRateSamples.Clear();
        _lastSample = null;
        TransferSpeed = _transfersData ? ZeroRate : string.Empty;
    }

    private void EndTransferRateTracking()
    {
        StopStalenessTimer();
        _transfersData = false;
        ResetTransferRate();
    }

    private void StartStalenessTimer()
    {
        StopStalenessTimer();
        // Captured on the UI thread, matching how Progress<T> marshals Update back to it.
        _syncContext = SynchronizationContext.Current;
        _stalenessTimer = _timeProvider.CreateTimer(
            _ => PostStalenessTick(), null, StalenessTickInterval, StalenessTickInterval);
    }

    private void StopStalenessTimer()
    {
        _stalenessTimer?.Dispose();
        _stalenessTimer = null;
        _syncContext = null;
    }

    private void PostStalenessTick()
    {
        // Read once: the timer callback runs on the thread pool and can race a concurrent
        // StopStalenessTimer. A tick that arrives after the operation ends is dropped by
        // TickStaleness's IsActive guard.
        var context = _syncContext;
        if (context is null)
            TickStaleness();
        else
            context.Post(static state => ((OperationProgressViewModel)state!).TickStaleness(), this);
    }

    [RelayCommand]
    private void Pause()
    {
        if (_pauseTokenSource is null || IsPaused) return;
        _pauseTokenSource.Pause();
        IsPaused = true;
        StatusText = "Paused";
        ResetTransferRate();
    }

    [RelayCommand]
    private void Resume()
    {
        if (_pauseTokenSource is null || !IsPaused) return;
        IsPaused = false;
        _pauseTokenSource.Resume();
        StatusText = "Resuming...";
        _lastReportTimestamp = _timeProvider.GetTimestamp();
        ResetTransferRate();
    }

    [RelayCommand]
    private void Cancel()
    {
        _pauseTokenSource?.Resume();
        _cancellationTokenSource?.Cancel();
        Cancelled();
    }

    private readonly record struct TransferRateSample(TimeSpan Elapsed, long TotalBytes);
}
