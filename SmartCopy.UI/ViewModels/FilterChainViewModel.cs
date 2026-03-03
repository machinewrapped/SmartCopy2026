using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels;

/// <summary>
/// Wraps a live <see cref="IFilter"/> instance and exposes observable properties
/// for binding to a filter card in <see cref="Views.FilterChainView"/>.
/// </summary>
public partial class FilterViewModel(IFilter filter) : ViewModelBase
{
    private IFilter _backingFilter = filter;

    /// <summary>The underlying filter instance.</summary>
    public IFilter BackingFilter => _backingFilter;

    /// <summary>
    /// Card title: uses the custom name if set, otherwise auto-generates "{Mode} {TypeDisplayName}".
    /// </summary>
    public string Summary
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_backingFilter.CustomName))
                return _backingFilter.CustomName;
            return $"{_backingFilter.Mode} {_backingFilter.TypeDisplayName}";
        }
    }

    /// <summary>Technical spec shown as a subtitle, e.g. "Extension: mp3; flac".</summary>
    public string Description => _backingFilter.Description;

    /// <summary>"INCLUDE" or "EXCLUDE".</summary>
    public string Mode => _backingFilter.Mode.ToString().ToUpper();

    /// <summary>
    /// Toggles the filter on/off. Mutates the underlying <see cref="IFilter"/> directly
    /// (FilterBase.IsEnabled is settable) and fires <see cref="IsEnabledChanged"/>.
    /// </summary>
    public bool IsEnabled
    {
        get => _backingFilter.IsEnabled;
        set
        {
            if (_backingFilter.IsEnabled == value) return;
            _backingFilter.IsEnabled = value;
            OnPropertyChanged();
            IsEnabledChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised when <see cref="IsEnabled"/> changes so the chain can be re-evaluated.</summary>
    internal event EventHandler? IsEnabledChanged;

    /// <summary>
    /// Replaces the wrapped filter instance (e.g. after an edit dialog) and refreshes
    /// all observable properties.
    /// </summary>
    internal void ReplaceFilter(IFilter newFilter)
    {
        _backingFilter = newFilter;
        OnPropertyChanged(nameof(BackingFilter));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(IsEnabled));
    }
}

public partial class FilterChainViewModel : ViewModelBase
{
    public ObservableCollection<FilterViewModel> Filters { get; } = [];

    [ObservableProperty]
    private FilterViewModel? _selectedFilter;

    /// <summary>
    /// Pushed by MainViewModel whenever the pipeline's first destination path changes.
    /// Used to pre-populate the mirror filter editor suggestion and the filter card description.
    /// </summary>
    [ObservableProperty]
    private string _pipelineDestinationPath = string.Empty;

    // ---- AddFilter flyout child VM ----
    public AddFilterViewModel AddFilter { get; }

    /// <summary>Preset store — exposed so code-behind can call SaveUserPresetAsync.</summary>
    public FilterPresetStore PresetStore { get; }

    /// <summary>Fired whenever the chain changes: add, remove, reorder, toggle, or edit.</summary>
    public event EventHandler? ChainChanged;

    /// <summary>
    /// Raised when the user clicks "＋ Configure..." in the flyout.
    /// The code-behind opens <see cref="Views.EditFilterDialog"/> and calls back with the result.
    /// </summary>
    public event EventHandler<string>? NewFilterDialogRequested;

    /// <summary>
    /// Raised when the user clicks the edit pencil on an existing filter card.
    /// EventArgs = the FilterViewModel to edit.
    /// </summary>
    public event EventHandler<FilterViewModel>? EditFilterRequested;

    /// <summary>Raised when the user clicks Save ▾; code-behind handles the file picker.</summary>
    public event EventHandler? SaveChainRequested;

    /// <summary>Raised when the user clicks Load ▾; code-behind handles the file picker.</summary>
    public event EventHandler? LoadChainRequested;

    public event EventHandler<bool>? VisibilityToggled;

    private bool _showExcludedNodesInTree = true;
    public bool ShowExcludedNodesInTree
    {
        get => _showExcludedNodesInTree;
        set
        {
            if (SetProperty(ref _showExcludedNodesInTree, value))
                VisibilityToggled?.Invoke(this, value);
        }
    }

    public FilterChainViewModel() : this(new FilterPresetStore(), new AppSettings()) { }

    public FilterChainViewModel(FilterPresetStore presetStore, AppSettings settings)
    {
        PresetStore = presetStore;
        _showExcludedNodesInTree = settings.ShowFilteredNodesInTree;
        AddFilter = new AddFilterViewModel(presetStore, settings);
        AddFilter.PresetPicked += OnPresetPicked;
        AddFilter.NewFilterRequested += typeKey => NewFilterDialogRequested?.Invoke(this, typeKey);
    }

    /// <summary>
    /// Builds a snapshot <see cref="FilterChain"/> from the current filter list.
    /// Called by <see cref="ViewModels.MainViewModel"/> whenever <see cref="ChainChanged"/> fires.
    /// </summary>
    public FilterChain BuildLiveChain()
        => new(Filters.Select(vm => vm.BackingFilter));

    /// <summary>Adds a new filter card from a freshly-built <see cref="IFilter"/>.</summary>
    public void AddFilterFromResult(IFilter filter)
    {
        var vm = new FilterViewModel(filter);
        vm.IsEnabledChanged += (_, _) => ChainChanged?.Invoke(this, EventArgs.Empty);
        Filters.Add(vm);

        ChainChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Replaces the filter wrapped by an existing card (edit dialog result).</summary>
    public void ReplaceFilter(FilterViewModel filterVm, IFilter newFilter)
    {
        filterVm.ReplaceFilter(newFilter);
        ChainChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reorders filter cards after a drag-drop operation.</summary>
    public void MoveFilter(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Filters.Count) return;
        if (toIndex < 0 || toIndex >= Filters.Count) return;
        if (fromIndex == toIndex) return;
        Filters.Move(fromIndex, toIndex);
        ChainChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPresetPicked(FilterPreset preset)
    {
        var filter = FilterFactory.FromConfig(preset.Config);
        AddFilterFromResult(filter);
    }

    [RelayCommand]
    private void RequestEditFilter(FilterViewModel filter)
    {
        EditFilterRequested?.Invoke(this, filter);
    }

    [RelayCommand]
    private void RemoveFilter(FilterViewModel filter)
    {
        if (filter is null) return;
        Filters.Remove(filter);
        ChainChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SaveChain()
    {
        SaveChainRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void LoadChain()
    {
        LoadChainRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnPipelineDestinationPathChanged(string value)
    {
        // Let live-wiring in MainViewModel re-evaluate the mirror filter description
        // by firing ChainChanged so it rebuilds and re-applies.
        ChainChanged?.Invoke(this, EventArgs.Empty);
    }
}
