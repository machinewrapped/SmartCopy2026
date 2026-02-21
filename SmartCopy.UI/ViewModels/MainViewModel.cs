using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartCopy.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _sourcePath = string.Empty;

    public DirectoryTreeViewModel DirectoryTree { get; } = new();
    public FileListViewModel FileList { get; } = new();
    public FilterChainViewModel FilterChain { get; } = new();
    public PipelineViewModel Pipeline { get; } = new();
    public OperationProgressViewModel OperationProgress { get; } = new();
    public PreviewViewModel Preview { get; } = new();

    public MainViewModel()
    {
        SourcePath = "/home/user/Music";

        // Propagate the pipeline's first Copy/Move destination to the filter chain.
        // This is also where a directory tree rescan will be triggered in future phases.
        Pipeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PipelineViewModel.FirstDestinationPath))
                FilterChain.PipelineDestinationPath = Pipeline.FirstDestinationPath;
        };
        FilterChain.PipelineDestinationPath = Pipeline.FirstDestinationPath;
    }
}
