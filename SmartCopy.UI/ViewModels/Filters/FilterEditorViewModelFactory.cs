using System;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Filters;

/// <summary>
/// Creates the correct <see cref="FilterEditorViewModelBase"/> subclass for a given filter type.
/// </summary>
public static class FilterEditorViewModelFactory
{
    public static FilterEditorViewModelBase Create(string filterType, AppSettings? settings = null) => filterType switch
    {
        "Extension" => new ExtensionFilterEditorViewModel(),
        "Wildcard"  => new WildcardFilterEditorViewModel(),
        "DateRange" => new DateRangeFilterEditorViewModel(),
        "SizeRange" => new SizeRangeFilterEditorViewModel(),
        "Mirror"    => new MirrorFilterEditorViewModel(settings),
        "Attribute" => new AttributeFilterEditorViewModel(),
        _ => throw new InvalidOperationException($"Unknown filter type: {filterType}")
    };

    public static FilterEditorViewModelBase CreateFrom(IFilter filter, AppSettings? settings = null)
    {
        var editor = Create(filter.Config.FilterType, settings);
        editor.LoadFrom(filter);
        return editor;
    }
}
