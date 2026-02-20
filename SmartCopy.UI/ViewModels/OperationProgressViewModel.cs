using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartCopy.UI.ViewModels;

public partial class OperationProgressViewModel : ViewModelBase
{
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

    public OperationProgressViewModel()
    {
        IsActive = true;
        PercentComplete = 78;
        StatusText = "142 files (2.3 GB) selected   17 filtered out";
        TimeRemaining = "0:34 left";
        CurrentFile = "Abbey Road/01 Come Together.flac";
    }
}
