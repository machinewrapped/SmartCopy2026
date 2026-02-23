using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels;

/// <summary>
/// Item in the Level 1 filter type selection list.
/// </summary>
public sealed record FilterTypeItem(string TypeKey, string DisplayName, string Description);

/// <summary>
/// Item in the Level 2 preset picker.
/// </summary>
public sealed record PresetItem(FilterPreset Preset, bool IsRecent)
{
    public string DisplayName => Preset.IsBuiltIn ? $"★ {Preset.Name}" : Preset.Name;
    public bool IsUserDefined => !Preset.IsBuiltIn;
}

/// <summary>
/// Drives the two-level "Add Filter" flyout:
/// Level 1 = filter type selector; Level 2 = preset picker for the chosen type.
/// </summary>
public partial class AddFilterViewModel : ObservableObject
{
    private readonly FilterPresetStore _presetStore;
    private readonly AppSettings _settings;
    private readonly string? _presetStorePath;

    public AddFilterViewModel(
        FilterPresetStore presetStore,
        AppSettings settings,
        string? presetStorePath = null)
    {
        _presetStore = presetStore;
        _settings = settings;
        _presetStorePath = presetStorePath;
    }

    // -------------------------------------------------------------------------
    // Level 1
    // -------------------------------------------------------------------------

    public IReadOnlyList<FilterTypeItem> FilterTypes { get; } =
    [
        new("Extension",  "Extension",   "Filter by file extension"),
        new("Wildcard",   "Wildcard",    "Filter by name pattern"),
        new("DateRange",  "Date Range",  "Filter by created/modified date"),
        new("SizeRange",  "Size Range",  "Filter by file size"),
        new("Mirror",     "Mirror",      "Skip files already on target"),
        new("Attribute",  "Attribute",   "Filter by file attributes"),
    ];

    // -------------------------------------------------------------------------
    // Navigation state
    // -------------------------------------------------------------------------

    [ObservableProperty]
    private bool _isLevel2Visible;

    [ObservableProperty]
    private FilterTypeItem? _selectedType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPresets))]
    private IReadOnlyList<PresetItem> _presetsForType = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecentPresets))]
    private IReadOnlyList<PresetItem> _recentPresetsForType = [];

    public bool HasPresets => PresetsForType.Count > 0;
    public bool HasRecentPresets => RecentPresetsForType.Count > 0;

    // -------------------------------------------------------------------------
    // Commands
    // -------------------------------------------------------------------------

    [RelayCommand]
    private async Task SelectFilterTypeAsync(FilterTypeItem type)
    {
        SelectedType = type;
        await LoadPresetsAsync(type.TypeKey);

        if (PresetsForType.Count == 0 && RecentPresetsForType.Count == 0)
        {
            var typeKey = type.TypeKey;
            GoBack();
            NewFilterRequested?.Invoke(typeKey);
            return;
        }

        IsLevel2Visible = true;
    }

    [RelayCommand]
    private void GoBack()
    {
        IsLevel2Visible = false;
        SelectedType = null;
        PresetsForType = [];
        RecentPresetsForType = [];
    }

    [RelayCommand]
    private void PickPreset(PresetItem item)
    {
        UpdateMru(SelectedType!.TypeKey, item.Preset.Id);
        PresetPicked?.Invoke(item.Preset);
        // Reset flyout to Level 1 ready for next use
        GoBack();
    }

    [RelayCommand]
    private void RequestNewFilter()
    {
        var typeKey = SelectedType!.TypeKey;
        GoBack();
        NewFilterRequested?.Invoke(typeKey);
    }

    [RelayCommand]
    private async Task DeletePresetAsync(PresetItem item)
    {
        await _presetStore.DeleteUserPresetAsync(SelectedType!.TypeKey, item.Preset.Id, _presetStorePath);

        // Remove from MRU if present
        if (_settings.FilterTypeMruPresetIds.TryGetValue(SelectedType.TypeKey, out var mru))
            mru.Remove(item.Preset.Id);

        // Refresh the preset lists in place
        await LoadPresetsAsync(SelectedType.TypeKey);
    }

    [RelayCommand]
    private void Close()
    {
        GoBack();
        CloseRequested?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Events raised for the parent (FilterChainViewModel / code-behind)
    // -------------------------------------------------------------------------

    /// <summary>Raised when the user picks a preset. The caller adds it to the chain directly.</summary>
    public event Action<FilterPreset>? PresetPicked;

    /// <summary>Raised when the user chooses "＋ New..." for a given filter type.</summary>
    public event Action<string>? NewFilterRequested;

    /// <summary>Raised when the user dismisses the flyout without picking anything.</summary>
    public event Action? CloseRequested;

    // -------------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------------

    private async Task LoadPresetsAsync(string filterType, CancellationToken ct = default)
    {
        var all = await _presetStore.GetPresetsForTypeAsync(filterType, _presetStorePath, ct);

        var mruIds = _settings.FilterTypeMruPresetIds.TryGetValue(filterType, out var ids)
            ? ids
            : [];

        var recentItems = mruIds
            .Select(id => all.FirstOrDefault(p => p.Id == id))
            .Where(p => p is not null)
            .Select(p => new PresetItem(p!, IsRecent: true))
            .ToList();

        var nonRecentItems = all
            .Where(p => !mruIds.Contains(p.Id))
            .Select(p => new PresetItem(p, IsRecent: false))
            .ToList();

        RecentPresetsForType = recentItems;
        PresetsForType = nonRecentItems;
    }

    internal void UpdateMru(string filterType, string presetId)
    {
        if (!_settings.FilterTypeMruPresetIds.TryGetValue(filterType, out var list))
        {
            list = [];
            _settings.FilterTypeMruPresetIds[filterType] = list;
        }

        list.Remove(presetId);
        list.Insert(0, presetId);

        const int MaxMru = 5;
        while (list.Count > MaxMru)
        {
            list.RemoveAt(list.Count - 1);
        }
    }
}
