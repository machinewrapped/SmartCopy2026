using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Progress;

namespace SmartCopy.UI.ViewModels;

public partial class OperationProgressViewModel : ViewModelBase
{
    private static readonly TimeSpan TransferRateWindow = TimeSpan.FromSeconds(10);

    private CancellationTokenSource? _cancellationTokenSource;
    private PauseTokenSource? _pauseTokenSource;
    private readonly Queue<TransferRateSample> _transferRateSamples = new();

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
        TransferSpeed = string.Empty;
        _transferRateSamples.Clear();

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
        TransferSpeed = string.Empty;
        _transferRateSamples.Clear();

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
        TransferSpeed = string.Empty;
        _transferRateSamples.Clear();

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

    private void UpdateTransferSpeed(OperationProgress progress)
    {
        if (_transferRateSamples.Count > 0)
        {
            var latest = _transferRateSamples.Last();
            if (progress.Elapsed < latest.Elapsed || progress.TotalBytesCompleted < latest.TotalBytes)
                _transferRateSamples.Clear();
        }

        _transferRateSamples.Enqueue(new TransferRateSample(progress.Elapsed, progress.TotalBytesCompleted));

        while (_transferRateSamples.Count > 1 &&
               progress.Elapsed - _transferRateSamples.Peek().Elapsed > TransferRateWindow)
        {
            _transferRateSamples.Dequeue();
        }

        if (_transferRateSamples.Count < 2)
        {
            TransferSpeed = string.Empty;
            return;
        }

        var first = _transferRateSamples.Peek();
        var elapsed = progress.Elapsed - first.Elapsed;
        TransferSpeed = elapsed.TotalSeconds > 0
            ? FileSizeFormatter.FormatRate((progress.TotalBytesCompleted - first.TotalBytes) / elapsed.TotalSeconds)
            : string.Empty;
    }

    private readonly record struct TransferRateSample(TimeSpan Elapsed, long TotalBytes);

    [RelayCommand]
    private void Pause()
    {
        if (_pauseTokenSource is null || IsPaused) return;
        _pauseTokenSource.Pause();
        IsPaused = true;
        StatusText = "Paused";
        TransferSpeed = string.Empty;
        _transferRateSamples.Clear();
    }

    [RelayCommand]
    private void Resume()
    {
        if (_pauseTokenSource is null || !IsPaused) return;
        IsPaused = false;
        _pauseTokenSource.Resume();
        StatusText = "Resuming...";
        TransferSpeed = string.Empty;
        _transferRateSamples.Clear();
    }

    [RelayCommand]
    private void Cancel()
    {
        _pauseTokenSource?.Resume();
        _cancellationTokenSource?.Cancel();
        Cancelled();
    }
}
