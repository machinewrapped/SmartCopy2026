using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Workflows;

namespace SmartCopy.UI.ViewModels.Workflows;

public partial class WorkflowMenuViewModel : ViewModelBase
{
    private readonly WorkflowPresetStore _store;

    public ObservableCollection<WorkflowPreset> SavedWorkflows { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveWorkflowCommand))]
    private bool _canSave;

    public event EventHandler? SaveRequested;
    public event EventHandler<string>? LoadRequested;
    public event EventHandler? ManageRequested;

    public WorkflowMenuViewModel(WorkflowPresetStore store)
    {
        _store = store;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveWorkflow() => SaveRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void LoadWorkflow(string name) => LoadRequested?.Invoke(this, name);

    [RelayCommand]
    private void ManageWorkflows() => ManageRequested?.Invoke(this, EventArgs.Empty);

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var presets = await _store.GetUserPresetsAsync(ct);
        SavedWorkflows.Clear();
        foreach (var p in presets)
        {
            SavedWorkflows.Add(p);
        }
    }
}
