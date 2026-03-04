using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SmartCopy.UI.ViewModels.Dialogs;

public partial class SaveFilterChainDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    private string _chainName = string.Empty;

    [ObservableProperty]
    private string? _selectedExistingName;

    public ObservableCollection<string> ExistingNames { get; } = [];

    public bool IsOverwrite =>
        ExistingNames.Any(n => string.Equals(n, ChainName?.Trim(), StringComparison.OrdinalIgnoreCase));

    public event Action? OkRequested;
    public event Action? CancelRequested;

    partial void OnSelectedExistingNameChanged(string? value)
    {
        if (value is not null)
        {
            ChainName = value;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOk))]
    private void Ok() => OkRequested?.Invoke();

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke();

    private bool CanOk => !string.IsNullOrWhiteSpace(ChainName);
}
