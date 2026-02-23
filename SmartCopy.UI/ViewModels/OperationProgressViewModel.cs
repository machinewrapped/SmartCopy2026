using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Progress;

namespace SmartCopy.UI.ViewModels;

public partial class OperationProgressViewModel : ViewModelBase
{
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private double _percentComplete;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _currentFile = string.Empty;

    [ObservableProperty]
    private string _timeRemaining = string.Empty;

    public void Begin(CancellationTokenSource cancellationTokenSource)
    {
        _cancellationTokenSource = cancellationTokenSource;
        IsActive = true;
        PercentComplete = 0;
        StatusText = "Starting operation...";
        CurrentFile = string.Empty;
        TimeRemaining = string.Empty;
    }

    public void Complete()
    {
        IsActive = false;
        StatusText = "Completed";
        TimeRemaining = "0:00 left";
        _cancellationTokenSource = null;
    }

    public void Cancelled()
    {
        IsActive = false;
        StatusText = "Cancelled";
        TimeRemaining = string.Empty;
        _cancellationTokenSource = null;
    }

    public void Update(OperationProgress progress)
    {
        CurrentFile = progress.CurrentFile;
        PercentComplete = progress.TotalBytes <= 0
            ? 0
            : Math.Round((double)progress.TotalBytesCompleted / progress.TotalBytes * 100, 2);
        StatusText = $"{progress.FilesCompleted}/{progress.FilesTotal} files";
        TimeRemaining = $"{progress.EstimatedRemaining:mm\\:ss} left";
    }

    [RelayCommand]
    private void Pause()
    {
        StatusText = "Pause is not implemented in Phase 1.";
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        Cancelled();
    }
}
