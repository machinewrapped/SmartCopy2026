using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Settings;
using SmartCopy.UI.Services;

namespace SmartCopy.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _sourcePath = string.Empty;

    private readonly MemoryFileSystemProvider _memoryProvider;
    private CancellationTokenSource? _filterCts;

    public DirectoryTreeViewModel DirectoryTree { get; }
    public FileListViewModel FileList { get; }
    public FilterChainViewModel FilterChain { get; }
    public PipelineViewModel Pipeline { get; } = new();
    public OperationProgressViewModel OperationProgress { get; } = new();
    public PreviewViewModel Preview { get; } = new();

    public MainViewModel()
    {
        var presetStore = new FilterPresetStore();
        var settings = new AppSettings();

        _memoryProvider = MockMemoryFileSystemFactory.CreateSeeded();
        SourcePath = MockMemoryFileSystemFactory.RootPath + "/";

        FilterChain = new FilterChainViewModel(presetStore, settings);
        DirectoryTree = new DirectoryTreeViewModel(_memoryProvider, MockMemoryFileSystemFactory.RootPath)
        {
            ShowFilteredNodesInTree = settings.ShowFilteredNodesInTree
        };

        FilterChain.VisibilityToggled += (_, isVisible) =>
        {
            DirectoryTree.ShowFilteredNodesInTree = isVisible;
            settings.ShowFilteredNodesInTree = isVisible;
        };

        FileList = new FileListViewModel(_memoryProvider, MockMemoryFileSystemFactory.DefaultFileListPath);

        // Propagate the pipeline's first Copy/Move destination to the filter chain.
        Pipeline.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PipelineViewModel.FirstDestinationPath))
                FilterChain.PipelineDestinationPath = Pipeline.FirstDestinationPath;
        };
        FilterChain.PipelineDestinationPath = Pipeline.FirstDestinationPath;

        // Subscribe to chain changes — re-evaluate filters within ~100 ms.
        FilterChain.ChainChanged += OnChainChanged;

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
    }

    private async void OnChainChanged(object? sender, EventArgs e)
    {
        // Debounce: cancel any in-flight apply and wait ~100 ms before re-evaluating.
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = new CancellationTokenSource();
        var ct = _filterCts.Token;

        try
        {
            await Task.Delay(100, ct);
            await ApplyFiltersAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    private async Task ApplyFiltersAsync(CancellationToken ct = default)
    {
        var chain = FilterChain.BuildLiveChain();
        FileList.UpdateChain(chain, _memoryProvider);

        await DirectoryTree.ApplyFiltersAsync(chain, _memoryProvider, ct);
        await FileList.ReapplyFiltersAsync(ct);
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
        // Phase 1: hardcode /mem/Mirror as the mirror-filter comparison path.
        FilterChain.PipelineDestinationPath = MockMemoryFileSystemFactory.TargetPath;

        // Pre-wire the chain before the initial tree load so the first file list load
        // already has a chain to evaluate.
        var chain = FilterChain.BuildLiveChain();
        FileList.UpdateChain(chain, _memoryProvider);

        await DirectoryTree.InitializeAsync(MockMemoryFileSystemFactory.DefaultFileListPath);

        if (DirectoryTree.SelectedNode != null)
            await FileList.LoadFilesForNodeAsync(DirectoryTree.SelectedNode);

        // Apply filters to the freshly loaded tree.
        await ApplyFiltersAsync();
    }
}
