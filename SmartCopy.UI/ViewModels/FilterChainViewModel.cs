using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartCopy.UI.ViewModels;

public partial class FilterViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _mode = "INCLUDE";
}

public partial class FilterChainViewModel : ViewModelBase
{
    public ObservableCollection<FilterViewModel> Filters { get; } = new();

    public FilterChainViewModel()
    {
        Filters.Add(new FilterViewModel { Description = "Extension: *.mp3;*.flac", Mode = "INCLUDE ▾" });
        Filters.Add(new FilterViewModel { Description = "Mirror: /target name+size", Mode = "EXCLUDE matched ▾" });
    }
}
