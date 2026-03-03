using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Progress;

namespace SmartCopy.UI.ViewModels;

public partial class OperationProgressViewModel : ViewModelBase
{
    private CancellationTokenSource? _cancellationTokenSource;
    private PauseTokenSource? _pauseTokenSource;

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

    public PipelineJob Begin(PipelineJob job)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _pauseTokenSource = new PauseTokenSource();
        IsActive = true;
        IsPaused = false;
        PercentComplete = 0;
        StatusText = "Starting operation...";
        CurrentFile = string.Empty;
        TimeRemaining = string.Empty;

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
    }

    [RelayCommand]
    private void Pause()
    {
        if (_pauseTokenSource is null || IsPaused) return;
        _pauseTokenSource.Pause();
        IsPaused = true;
        StatusText = "Paused";
    }

    [RelayCommand]
    private void Resume()
    {
        if (_pauseTokenSource is null || !IsPaused) return;
        IsPaused = false;
        _pauseTokenSource.Resume();
        StatusText = "Resuming...";
    }

    [RelayCommand]
    private void Cancel()
    {
        _pauseTokenSource?.Resume();
        _cancellationTokenSource?.Cancel();
        Cancelled();
    }
}
