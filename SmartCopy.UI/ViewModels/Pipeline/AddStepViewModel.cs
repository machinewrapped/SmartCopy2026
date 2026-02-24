using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
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
public sealed record StepPresetItem(StepPreset Preset, bool IsRecent, string StepType)
{
    public string DisplayName => Preset.IsBuiltIn ? $"★ {Preset.Name}" : Preset.Name;
    public bool IsUserDefined => !Preset.IsBuiltIn;
}

/// <summary>
/// A menu item to be shown in a MenuFlyout for adding steps.
/// </summary>
public sealed record AddStepMenuItem(
    string Header,
    ICommand? Command = null,
    object? CommandParameter = null,
    IReadOnlyList<AddStepMenuItem>? Items = null,
    bool IsEnabled = true,
    bool IsSeparator = false,
    bool IsUserDefined = false,
    ICommand? DeleteCommand = null,
    object? DeleteCommandParameter = null)
{
    public static AddStepMenuItem Separator() => new("-", IsSeparator: true, IsEnabled: false);
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
        _ = InitializeMenusAsync();
    }

    [ObservableProperty]
    private IReadOnlyList<AddStepMenuItem> _copyMenuItems = [];

    [ObservableProperty]
    private IReadOnlyList<AddStepMenuItem> _moveMenuItems = [];

    [ObservableProperty]
    private IReadOnlyList<AddStepMenuItem> _deleteMenuItems = [];

    [ObservableProperty]
    private IReadOnlyList<AddStepMenuItem> _pathMenuItems = [];

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

    /// <summary>Level 1 should be visible when Saving is not visible.</summary>
    public bool IsLevel1Visible => !IsSavingPipeline;

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
    private void RequestNewStep(StepKind kind)
    {
        ResetToLevel1();
        StepTypeSelected?.Invoke(kind);
    }

    [RelayCommand]
    private void PickPreset(StepPresetItem item)
    {
        UpdateMru(item.StepType, item.Preset.Id);
        StepPresetPicked?.Invoke(item.Preset);
        ResetToLevel1();
    }

    [RelayCommand]
    private async Task DeletePresetAsync(StepPresetItem item)
    {
        await _presetStore.DeleteUserPresetAsync(
            item.StepType, item.Preset.Id, _presetStorePath);

        if (_settings.StepTypeMruPresetIds.TryGetValue(item.StepType, out var mru))
            mru.Remove(item.Preset.Id);

        // Rebuild menus after a preset is deleted
        _ = InitializeMenusAsync();
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
        IsSavingPipeline = false;
        NewPipelineName = string.Empty;
    }

    private async Task InitializeMenusAsync()
    {
        CopyMenuItems = await BuildStepMenuAsync(StepKind.Copy);
        MoveMenuItems = await BuildStepMenuAsync(StepKind.Move);
        DeleteMenuItems = await BuildStepMenuAsync(StepKind.Delete);
        PathMenuItems = await BuildPathCategoryMenuAsync();
    }

    private async Task<IReadOnlyList<AddStepMenuItem>> BuildStepMenuAsync(StepKind kind)
    {
        var stepType = kind.ToString();
        var all = await _presetStore.GetPresetsForTypeAsync(stepType, _presetStorePath);

        var mruIds = _settings.StepTypeMruPresetIds.TryGetValue(stepType, out var ids) ? ids : [];

        var recentItems = mruIds
            .Select(id => all.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null)
            .Select(p => new StepPresetItem(p!, IsRecent: true, stepType))
            .ToList();

        var nonRecentItems = all
            .Where(p => !mruIds.Contains(p.Id))
            .Select(p => new StepPresetItem(p, IsRecent: false, stepType))
            .ToList();

        var list = new List<AddStepMenuItem>();
        
        list.Add(new AddStepMenuItem("＋ New...", (ICommand)RequestNewStepCommand, kind));

        if (recentItems.Count > 0)
        {
            list.Add(AddStepMenuItem.Separator());
            list.Add(new AddStepMenuItem("Recently used", IsEnabled: false));
            foreach (var p in recentItems)
            {
                list.Add(new AddStepMenuItem(p.DisplayName, (ICommand)PickPresetCommand, p, 
                    IsUserDefined: p.IsUserDefined, 
                    DeleteCommand: (ICommand)DeletePresetCommand, DeleteCommandParameter: p));
            }
        }

        if (nonRecentItems.Count > 0)
        {
            list.Add(AddStepMenuItem.Separator());
            foreach (var p in nonRecentItems)
            {
                list.Add(new AddStepMenuItem(p.DisplayName, (ICommand)PickPresetCommand, p, 
                    IsUserDefined: p.IsUserDefined, 
                    DeleteCommand: (ICommand)DeletePresetCommand, DeleteCommandParameter: p));
            }
        }

        return list;
    }

    private async Task<IReadOnlyList<AddStepMenuItem>> BuildPathCategoryMenuAsync()
    {
        var list = new List<AddStepMenuItem>();
        var items = GetItemsForCategory(StepCategory.Path);
        foreach (var item in items)
        {
            var subMenu = await BuildStepMenuAsync(item.Kind);
            list.Add(new AddStepMenuItem(item.DisplayName, Items: subMenu));
        }
        return list;
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
