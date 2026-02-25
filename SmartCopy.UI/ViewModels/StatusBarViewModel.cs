using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartCopy.UI.ViewModels;

public partial class StatusBarViewModel : ViewModelBase
{
    public SelectionViewModel Selection { get; } = new();
    public OperationProgressViewModel Progress { get; }

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanStatusText = string.Empty;

    public bool IsIdle => !IsScanning && !Progress.IsActive;

    public StatusBarViewModel()
    {
        Progress = new OperationProgressViewModel();
        Progress.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OperationProgressViewModel.IsActive))
                OnPropertyChanged(nameof(IsIdle));
        };
    }

    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(IsIdle));
}
