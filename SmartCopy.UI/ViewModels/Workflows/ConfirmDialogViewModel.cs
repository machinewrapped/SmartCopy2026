using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SmartCopy.UI.ViewModels.Workflows;

public partial class ConfirmDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = "Confirm";

    [ObservableProperty]
    private string _message = "Are you sure?";

    [ObservableProperty]
    private string _confirmText = "OK";

    [ObservableProperty]
    private string _cancelText = "Cancel";

    public event Action? ConfirmRequested;
    public event Action? CancelRequested;

    [RelayCommand]
    private void Confirm() => ConfirmRequested?.Invoke();

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke();
}
