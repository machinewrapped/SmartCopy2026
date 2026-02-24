using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

/// <summary>
/// Item in the Level 2 step type selection list.
/// </summary>
public sealed record StepTypeItem(StepKind Kind, string DisplayName, string Description);

/// <summary>
/// Item in the Level 3 preset picker.
/// </summary>
public sealed record StepPresetItem(StepPreset Preset, bool IsRecent)
{
    public string DisplayName => Preset.IsBuiltIn ? $"★ {Preset.Name}" : Preset.Name;
    public bool IsUserDefined => !Preset.IsBuiltIn;
}

/// <summary>
/// Drives the three-level "Add Step" flyout:
/// Level 1 = category selector; Level 2 = step type selector; Level 3 = preset picker.
/// </summary>
public partial class AddStepViewModel : ObservableObject
{
    private readonly StepPresetStore _presetStore;
    private readonly AppSettings _settings;
    private readonly string? _presetStorePath;

    public AddStepViewModel(
        StepPresetStore? presetStore = null,
        AppSettings? settings = null,
        string? presetStorePath = null)
    {
        _presetStore = presetStore ?? new StepPresetStore();
        _settings = settings ?? new AppSettings();
        _presetStorePath = presetStorePath;
    }

    // -------------------------------------------------------------------------
    // Level 1 → Level 2 navigation
    // -------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLevel1Visible))]
    private bool _isLevel2Visible;

    [ObservableProperty]
    private StepCategory? _selectedCategory;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStepTypeItems))]
    private IReadOnlyList<StepTypeItem> _stepTypeItems = [];

    public bool HasStepTypeItems => StepTypeItems.Count > 0;

    // -------------------------------------------------------------------------
    // Level 2 → Level 3 navigation
    // -------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLevel1Visible))]
    private bool _isLevel3Visible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedStepTypeName))]
    private StepTypeItem? _selectedStepType;

    public string SelectedStepTypeName => SelectedStepType?.DisplayName ?? string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPresets))]
    private IReadOnlyList<StepPresetItem> _presetsForType = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecentPresets))]
    private IReadOnlyList<StepPresetItem> _recentPresetsForType = [];

    // -------------------------------------------------------------------------
    // Pipeline presets (Level 1)
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private IReadOnlyList<PipelinePreset> _userPresets = [];

    [ObservableProperty]
    private bool _hasSteps;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLevel1Visible))]
    private bool _isSavingPipeline;

    [ObservableProperty]
    private string _newPipelineName = string.Empty;

    public bool HasPresets => PresetsForType.Count > 0;
    public bool HasRecentPresets => RecentPresetsForType.Count > 0;

    /// <summary>Level 2 should be visible only when Level 3 is not.</summary>
    public bool IsLevel2VisibleOnly => IsLevel2Visible && !IsLevel3Visible;

    /// <summary>Level 1 should be visible when neither Level 2, 3, nor Saving are visible.</summary>
    public bool IsLevel1Visible => !IsLevel2VisibleOnly && !IsLevel3Visible && !IsSavingPipeline;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>Raised when the user picks "＋ New..." or a type with no presets. Caller opens EditStepDialog.</summary>
    public event Action<StepKind>? StepTypeSelected;

    /// <summary>Raised when the user picks a step preset. Caller applies it directly.</summary>
    public event Action<StepPreset>? StepPresetPicked;

    public event Action<StepCategory>? CategoryNavigated;

    public event Action? CloseRequested;

    /// <summary>Raised when the user picks a pipeline preset.</summary>
    public event Action<string>? LoadPipelinePresetRequested;

    /// <summary>Raised when the user requests saving the current pipeline.</summary>
    public event Action<string?>? SavePipelineRequested;

    /// <summary>Raised when the user requests deleting a user pipeline preset.</summary>
    public event Action<string>? DeletePipelineRequested;

    // -------------------------------------------------------------------------
    // Commands
    // -------------------------------------------------------------------------

    [RelayCommand]
    private void NavigateToCategory(StepCategory category)
    {
        SelectedCategory = category;
        StepTypeItems = GetItemsForCategory(category);
        IsLevel2Visible = true;
        CategoryNavigated?.Invoke(category);
    }

    [RelayCommand]
    private async Task SelectStepTypeAsync(StepKind kind)
    {
        SelectedStepType = StepTypeItems.FirstOrDefault(i => i.Kind == kind);
        await LoadPresetsAsync(kind.ToString());

        if (PresetsForType.Count == 0 && RecentPresetsForType.Count == 0)
        {
            var capturedKind = kind;
            ResetToLevel1();
            StepTypeSelected?.Invoke(capturedKind);
            return;
        }

        IsLevel3Visible = true;
    }

    [RelayCommand]
    private async Task SelectTopLevelStepTypeAsync(StepKind kind)
    {
        SelectedCategory = null;
        SelectedStepType = GetItemsForCategory(StepCategory.Executable).FirstOrDefault(i => i.Kind == kind);
        await LoadPresetsAsync(kind.ToString());

        if (PresetsForType.Count == 0 && RecentPresetsForType.Count == 0)
        {
            var capturedKind = kind;
            ResetToLevel1();
            StepTypeSelected?.Invoke(capturedKind);
            return;
        }

        IsLevel2Visible = true;
        IsLevel3Visible = true;
    }

    [RelayCommand]
    private void PickPreset(StepPresetItem item)
    {
        UpdateMru(SelectedStepType!.Kind.ToString(), item.Preset.Id);
        StepPresetPicked?.Invoke(item.Preset);
        ResetToLevel1();
    }

    [RelayCommand]
    private void RequestNewStep()
    {
        var kind = SelectedStepType!.Kind;
        ResetToLevel1();
        StepTypeSelected?.Invoke(kind);
    }

    [RelayCommand]
    private async Task DeletePresetAsync(StepPresetItem item)
    {
        await _presetStore.DeleteUserPresetAsync(
            SelectedStepType!.Kind.ToString(), item.Preset.Id, _presetStorePath);

        if (_settings.StepTypeMruPresetIds.TryGetValue(SelectedStepType.Kind.ToString(), out var mru))
            mru.Remove(item.Preset.Id);

        await LoadPresetsAsync(SelectedStepType.Kind.ToString());
    }

    [RelayCommand]
    private void GoBackToLevel2()
    {
        IsLevel3Visible = false;
        SelectedStepType = null;
        PresetsForType = [];
        RecentPresetsForType = [];

        if (SelectedCategory == null)
        {
            IsLevel2Visible = false;
        }
    }

    [RelayCommand]
    private void GoBack()
    {
        GoBackToLevel2();
        IsLevel2Visible = false;
        SelectedCategory = null;
        StepTypeItems = [];
    }

    [RelayCommand]
    private void LoadPreset(string name)
    {
        LoadPipelinePresetRequested?.Invoke(name);
    }

    [RelayCommand]
    private void BeginSavePipeline()
    {
        IsSavingPipeline = true;
        NewPipelineName = string.Empty;
    }

    [RelayCommand]
    private void CancelSavePipeline()
    {
        IsSavingPipeline = false;
        NewPipelineName = string.Empty;
    }

    [RelayCommand]
    private void ConfirmSavePipeline()
    {
        var nameToSave = string.IsNullOrWhiteSpace(NewPipelineName) ? null : NewPipelineName;
        SavePipelineRequested?.Invoke(nameToSave);
        IsSavingPipeline = false;
        NewPipelineName = string.Empty;
    }

    [RelayCommand]
    private void Close()
    {
        RequestClose();
    }

    [RelayCommand]
    private void DeletePipeline(string name)
    {
        DeletePipelineRequested?.Invoke(name);
    }

    public void RequestClose()
    {
        ResetToLevel1();
        CloseRequested?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------------

    private void ResetToLevel1()
    {
        IsLevel3Visible = false;
        SelectedStepType = null;
        PresetsForType = [];
        RecentPresetsForType = [];
        IsLevel2Visible = false;
        SelectedCategory = null;
        StepTypeItems = [];
        IsSavingPipeline = false;
        NewPipelineName = string.Empty;
    }

    private async Task LoadPresetsAsync(string stepType, CancellationToken ct = default)
    {
        var all = await _presetStore.GetPresetsForTypeAsync(stepType, _presetStorePath, ct);

        var mruIds = _settings.StepTypeMruPresetIds.TryGetValue(stepType, out var ids)
            ? ids
            : [];

        var recentItems = mruIds
            .Select(id => all.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null)
            .Select(p => new StepPresetItem(p!, IsRecent: true))
            .ToList();

        var nonRecentItems = all
            .Where(p => !mruIds.Contains(p.Id))
            .Select(p => new StepPresetItem(p, IsRecent: false))
            .ToList();

        RecentPresetsForType = recentItems;
        PresetsForType = nonRecentItems;
    }

    internal void UpdateMru(string stepType, string presetId)
    {
        if (!_settings.StepTypeMruPresetIds.TryGetValue(stepType, out var list))
        {
            list = [];
            _settings.StepTypeMruPresetIds[stepType] = list;
        }

        list.Remove(presetId);
        list.Insert(0, presetId);

        const int MaxMru = 5;
        while (list.Count > MaxMru)
        {
            list.RemoveAt(list.Count - 1);
        }
    }

    private static IReadOnlyList<StepTypeItem> GetItemsForCategory(StepCategory category)
    {
        return category switch
        {
            StepCategory.Path =>
            [
                new(StepKind.Flatten, "Flatten", "Strip directory structure"),
                new(StepKind.Rebase, "Rebase", "Adjust path roots and prefixes"),
                new(StepKind.Rename, "Rename", "Rename using a pattern"),
            ],
            StepCategory.Content =>
            [
                new(StepKind.Convert, "Convert", "Convert content format"),
            ],
            StepCategory.Executable =>
            [
                new(StepKind.Copy, "Copy", "Copy to destination"),
                new(StepKind.Move, "Move", "Move to destination"),
                new(StepKind.Delete, "Delete", "Delete source file"),
            ],
            _ => [],
        };
    }
}
