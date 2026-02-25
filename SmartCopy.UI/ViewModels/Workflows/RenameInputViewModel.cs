using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SmartCopy.UI.ViewModels.Workflows;

public partial class RenameInputViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    private string? _newName;

    public event Action? OkRequested;
    public event Action? CancelRequested;

    [RelayCommand(CanExecute = nameof(CanOk))]
    private void Ok() => OkRequested?.Invoke();

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke();

    private bool CanOk => !string.IsNullOrWhiteSpace(NewName);
}
