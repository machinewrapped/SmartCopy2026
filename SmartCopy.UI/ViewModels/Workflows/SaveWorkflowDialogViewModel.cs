using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SmartCopy.UI.ViewModels.Workflows;

public partial class SaveWorkflowDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OkCommand))]
    private string _workflowName = string.Empty;

    [ObservableProperty]
    private string? _selectedExistingName;

    public ObservableCollection<string> ExistingNames { get; } = [];

    public bool IsOverwrite =>
        ExistingNames.Any(n => string.Equals(n, WorkflowName?.Trim(), StringComparison.OrdinalIgnoreCase));

    public event Action? OkRequested;
    public event Action? CancelRequested;

    partial void OnSelectedExistingNameChanged(string? value)
    {
        if (value is not null)
        {
            WorkflowName = value;
        }
    }

    [RelayCommand(CanExecute = nameof(CanOk))]
    private void Ok() => OkRequested?.Invoke();

    [RelayCommand]
    private void Cancel() => CancelRequested?.Invoke();

    private bool CanOk => !string.IsNullOrWhiteSpace(WorkflowName);
}
