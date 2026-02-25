using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Workflows;

namespace SmartCopy.UI.ViewModels.Workflows;

public partial class ManageWorkflowsDialogViewModel : ViewModelBase
{
    private readonly WorkflowPresetStore _store;
    private readonly string? _storeDirectory;

    [ObservableProperty]
    private WorkflowPreset? _selectedWorkflow;

    public ObservableCollection<WorkflowPreset> Workflows { get; } = [];

    public bool HasChanges { get; private set; }

    public event Action? CloseRequested;

    /// <summary>Raised when a rename needs a text input from the user (handled in code-behind).</summary>
    public event Func<string, Task<string?>>? RenameInputRequested;

    /// <summary>Raised when a delete needs confirmation (handled in code-behind).</summary>
    public event Func<string, Task<bool>>? DeleteConfirmRequested;

    public ManageWorkflowsDialogViewModel(WorkflowPresetStore store, string? storeDirectory = null)
    {
        _store = store;
        _storeDirectory = storeDirectory;
    }

    public async Task LoadAsync()
    {
        var presets = await _store.GetUserPresetsAsync(_storeDirectory);
        Workflows.Clear();
        foreach (var p in presets)
        {
            Workflows.Add(p);
        }
    }

    [RelayCommand]
    private async Task DeleteWorkflow(WorkflowPreset? workflow)
    {
        if (workflow is null) return;

        if (DeleteConfirmRequested is not null)
        {
            var confirmed = await DeleteConfirmRequested.Invoke(workflow.Name);
            if (!confirmed) return;
        }

        await _store.DeleteUserPresetAsync(workflow.Name, _storeDirectory);
        Workflows.Remove(workflow);
        HasChanges = true;
    }

    [RelayCommand]
    private async Task RenameWorkflow(WorkflowPreset? workflow)
    {
        if (workflow is null) return;

        string? newName = null;
        if (RenameInputRequested is not null)
        {
            newName = await RenameInputRequested.Invoke(workflow.Name);
        }

        if (string.IsNullOrWhiteSpace(newName)) return;

        await _store.RenameUserPresetAsync(workflow.Name, newName, _storeDirectory);
        workflow.Name = newName;
        HasChanges = true;

        // Refresh the list to update UI ordering
        await LoadAsync();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
