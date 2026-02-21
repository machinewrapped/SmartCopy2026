using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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

    // Pushed by MainViewModel whenever Pipeline.FirstDestinationPath changes.
    [ObservableProperty]
    private string _pipelineDestinationPath = string.Empty;

    // Reference to the Mirror filter stub so its description can be updated reactively.
    private FilterViewModel? _mirrorFilter;

    public FilterChainViewModel()
    {
        Filters.Add(new FilterViewModel
        {
            Summary     = "Only .mp3 and .flac files",
            Description = "Extension: *.mp3; *.flac",
            Mode        = "INCLUDE",
            IsEnabled   = true,
        });

        _mirrorFilter = new FilterViewModel
        {
            Summary   = "Skip files already on target",
            Mode      = "EXCLUDE",
            IsEnabled = true,
        };
        UpdateMirrorDescription();
        Filters.Add(_mirrorFilter);
    }

    partial void OnPipelineDestinationPathChanged(string value) => UpdateMirrorDescription();

    private void UpdateMirrorDescription()
    {
        if (_mirrorFilter is null) return;
        _mirrorFilter.Description = string.IsNullOrEmpty(PipelineDestinationPath)
            ? "Mirror: (no destination in pipeline)  match by name + size"
            : $"Mirror: {PipelineDestinationPath}  match by name + size";
    }

    [RelayCommand]
    private void AddFilter()
    {
        Filters.Add(new FilterViewModel
        {
            Summary     = "New Simulated Filter",
            Description = "Placeholder filter to test the UI flow.",
            Mode        = "INCLUDE",
            IsEnabled   = true,
        });
    }

    [RelayCommand]
    private void RemoveFilter(FilterViewModel filter)
    {
        if (filter != null)
            Filters.Remove(filter);
    }
}
