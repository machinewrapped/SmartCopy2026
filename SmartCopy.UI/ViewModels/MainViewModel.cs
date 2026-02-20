using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartCopy.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private string _targetPath = string.Empty;

    public DirectoryTreeViewModel DirectoryTree { get; } = new();
    public FileListViewModel FileList { get; } = new();
    public FilterChainViewModel FilterChain { get; } = new();
    public PipelineViewModel Pipeline { get; } = new();
    public OperationProgressViewModel OperationProgress { get; } = new();
    public PreviewViewModel Preview { get; } = new();

    public MainViewModel()
    {
        SourcePath = "/home/user/Music";
        TargetPath = "/mnt/phone/Music";
    }
}
