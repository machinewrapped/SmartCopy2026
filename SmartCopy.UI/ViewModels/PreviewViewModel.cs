using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartCopy.UI.ViewModels;

public partial class PreviewItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private string _destinationPath = string.Empty;

    [ObservableProperty]
    private string _action = string.Empty;
}

public partial class PreviewViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isOpen;

    public ObservableCollection<PreviewItemViewModel> Actions { get; } = new();

    public PreviewViewModel()
    {
        Actions.Add(new PreviewItemViewModel { SourcePath = "Rock/Beatles/Abbey Road/01 Come Together.flac", DestinationPath = "01 Come Together.mp3", Action = "Convert & Copy" });
    }
}
