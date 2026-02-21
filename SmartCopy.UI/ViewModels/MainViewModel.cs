using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Diagnostics;
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

        DirectoryTree.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(DirectoryTreeViewModel.SelectedNode))
            {
                var selectedNode = DirectoryTree.SelectedNode;
                if (selectedNode?.IsDirectory == true)
                {
                    try
                    {
                        await FileList.LoadFilesForNodeAsync(selectedNode);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to load files for directory: {ex}");
                    }
                }
            }
        };
        InitializeInBackground();

        // Propagate the pipeline's first Copy/Move destination to the filter chain.
        // This is also where a directory tree rescan will be triggered in future phases.
        Pipeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PipelineViewModel.FirstDestinationPath))
                FilterChain.PipelineDestinationPath = Pipeline.FirstDestinationPath;
        };
        FilterChain.PipelineDestinationPath = Pipeline.FirstDestinationPath;
    }

    private async void InitializeInBackground()
    {
        try
        {
            await InitializeAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Initialization failed: {ex}");
        }
    }

    private async Task InitializeAsync()
    {
        await DirectoryTree.InitializeAsync(MockMemoryFileSystemFactory.DefaultFileListPath);
        if (DirectoryTree.SelectedNode != null)
        {
            await FileList.LoadFilesForNodeAsync(DirectoryTree.SelectedNode);
        }
    }
}
