using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels;

public partial class ImportConflictDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Import Settings";

    [ObservableProperty]
    private string _message = "";

    public event Action<ConflictResolution>? Resolved;
    public event Action? CancelRequested;

    [RelayCommand]
    private void Overwrite() => Resolved?.Invoke(ConflictResolution.OverwriteAll);

    [RelayCommand]
    private void Skip() => Resolved?.Invoke(ConflictResolution.SkipExisting);

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke();
}
