using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Filters;

namespace SmartCopy.UI.ViewModels.Filters;

/// <summary>
/// Abstract base for all filter-type-specific editor view models.
/// One concrete subclass exists per filter type; the EditFilterDialog hosts the appropriate one.
/// </summary>
public abstract partial class FilterEditorViewModelBase : ObservableObject
{
    [ObservableProperty]
    private FilterMode _mode = FilterMode.Include;

    [ObservableProperty]
    private string _filterName = string.Empty;

    [ObservableProperty]
    private bool _saveAsPreset;

    private bool _userHasEditedName;

    partial void OnFilterNameChanged(string value)
    {
        _userHasEditedName = !string.IsNullOrEmpty(value);
    }

    /// <summary>
    /// Produces a validated filter instance from the current editor state.
    /// Only called when <see cref="IsValid"/> is true.
    /// </summary>
    public abstract IFilter BuildFilter();

    /// <summary>
    /// Whether the current inputs are sufficient to build a valid filter.
    /// </summary>
    public abstract bool IsValid { get; }

    /// <summary>
    /// Populates all editor fields from an existing filter (used when editing).
    /// </summary>
    public abstract void LoadFrom(IFilter filter);

    /// <summary>
    /// Auto-generates a human-readable name from the current parameter state.
    /// </summary>
    public virtual string GenerateName() => string.Empty;

    /// <summary>
    /// Call this from parameter change hooks to keep <see cref="FilterName"/> in sync
    /// unless the user has manually entered a name.
    /// </summary>
    protected void AutoUpdateName()
    {
        if (!_userHasEditedName)
        {
            FilterName = GenerateName();
        }
    }
}
