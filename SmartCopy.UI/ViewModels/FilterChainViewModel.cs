using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartCopy.UI.ViewModels;

public partial class FilterViewModel : ViewModelBase
{
    /// <summary>
    /// Human-friendly one-liner shown on the card face, e.g. "Only .mp3 and .flac files".
    /// </summary>
    [ObservableProperty]
    private string _summary = string.Empty;

    /// <summary>
    /// Technical description / raw filter spec shown as a subtitle below the summary.
    /// </summary>
    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>Whether the filter is active.</summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>INCLUDE or EXCLUDE mode.</summary>
    [ObservableProperty]
    private string _mode = "INCLUDE";
}

public partial class FilterChainViewModel : ViewModelBase
{
    public ObservableCollection<FilterViewModel> Filters { get; } = new();

    public FilterChainViewModel()
    {
        Filters.Add(new FilterViewModel
        {
            Summary     = "Only .mp3 and .flac files",
            Description = "Extension: *.mp3; *.flac",
            Mode        = "INCLUDE",
            IsEnabled   = true,
        });
        Filters.Add(new FilterViewModel
        {
            Summary     = "Skip files already on target",
            Description = "Mirror: /target  match by name + size",
            Mode        = "EXCLUDE",
            IsEnabled   = true,
        });
    }
}
