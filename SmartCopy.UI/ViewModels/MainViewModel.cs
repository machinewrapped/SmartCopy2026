using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;
using SmartCopy.UI.Services;

namespace SmartCopy.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _sourcePath = string.Empty;

    public DirectoryTreeViewModel DirectoryTree { get; }
    public FileListViewModel FileList { get; }
    public FilterChainViewModel FilterChain { get; } = new();
    public PipelineViewModel Pipeline { get; } = new();
    public OperationProgressViewModel OperationProgress { get; } = new();
    public PreviewViewModel Preview { get; } = new();

    public MainViewModel()
    {
        var memoryProvider = MockMemoryFileSystemFactory.CreateSeeded();
        SourcePath = MockMemoryFileSystemFactory.RootPath + "/";

        DirectoryTree = new DirectoryTreeViewModel(memoryProvider, MockMemoryFileSystemFactory.RootPath);
        FileList = new FileListViewModel(memoryProvider, MockMemoryFileSystemFactory.DefaultFileListPath);

        DirectoryTree.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(DirectoryTreeViewModel.SelectedNode))
            {
                var selectedNode = DirectoryTree.SelectedNode;
                if (selectedNode?.IsDirectory == true)
                {
                    _ = FileList.LoadFilesForDirectoryAsync(selectedNode.FullPath);
                }
            }
        };
        _ = InitializeAsync();

        // Propagate the pipeline's first Copy/Move destination to the filter chain.
        // This is also where a directory tree rescan will be triggered in future phases.
        Pipeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PipelineViewModel.FirstDestinationPath))
                FilterChain.PipelineDestinationPath = Pipeline.FirstDestinationPath;
        };
        FilterChain.PipelineDestinationPath = Pipeline.FirstDestinationPath;
    }

    private async Task InitializeAsync()
    {
        await DirectoryTree.InitializeAsync(MockMemoryFileSystemFactory.DefaultFileListPath);
        await FileList.LoadFilesForDirectoryAsync(MockMemoryFileSystemFactory.DefaultFileListPath);
    }
}
