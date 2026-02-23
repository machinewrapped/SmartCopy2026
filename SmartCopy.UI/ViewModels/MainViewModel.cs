using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Settings;
using SmartCopy.UI.Services;

namespace SmartCopy.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private string? _selectedSourceBookmark;

    private readonly MemoryFileSystemProvider _memoryProvider;
    private readonly AppSettings _settings = new();
    private readonly AppSettingsStore _settingsStore = new();
    private CancellationTokenSource? _filterCts;

    public ObservableCollection<string> SourceBookmarks { get; } = [];

    public DirectoryTreeViewModel DirectoryTree { get; }
    public FileListViewModel FileList { get; }
    public FilterChainViewModel FilterChain { get; }
    public PipelineViewModel Pipeline { get; } = new();
    public OperationProgressViewModel OperationProgress { get; } = new();
    public PreviewViewModel Preview { get; } = new();

    public MainViewModel()
    {
        var presetStore = new FilterPresetStore();

        _memoryProvider = MockMemoryFileSystemFactory.CreateSeeded();
        SourcePath = MockMemoryFileSystemFactory.SourcePath;

        FilterChain = new FilterChainViewModel(presetStore, _settings);
        DirectoryTree = new DirectoryTreeViewModel(_memoryProvider, MockMemoryFileSystemFactory.RootPath)
        {
            ShowFilteredNodesInTree = _settings.ShowFilteredNodesInTree
        };

        FilterChain.VisibilityToggled += (_, isVisible) =>
        {
            DirectoryTree.ShowFilteredNodesInTree = isVisible;
            _settings.ShowFilteredNodesInTree = isVisible;
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

    partial void OnSelectedSourceBookmarkChanged(string? value)
    {
        if (value is null) return;
        SourcePath = value;
        _ = ApplySourcePathCoreAsync(value);
    }

    [RelayCommand]
    private void RevertSourcePath()
    {
        SourcePath = DirectoryTree.RootNodes.FirstOrDefault()?.FullPath ?? SourcePath;
    }

    [RelayCommand]
    private async Task ApplySourcePath()
    {
        var path = SourcePath.Trim();
        if (string.IsNullOrWhiteSpace(path)) return;
        await ApplySourcePathCoreAsync(path);
    }

    private async Task ApplySourcePathCoreAsync(string path)
    {
        var previousPath = DirectoryTree.RootNodes.FirstOrDefault()?.FullPath ?? path;
        try
        {
            await DirectoryTree.ChangeRootAsync(path);
            RecordRecentSource(path);
            await ApplyFiltersAsync();
            _settings.LastSourcePath = path;
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to change source path to '{path}': {ex}");
            SourcePath = previousPath;
        }
    }

    [RelayCommand]
    private async Task BookmarkCurrentPath()
    {
        var path = SourcePath.Trim();
        if (string.IsNullOrWhiteSpace(path) || _settings.FavouritePaths.Contains(path))
            return;

        _settings.FavouritePaths.Insert(0, path);
        RefreshSourceBookmarks();
        await _settingsStore.SaveAsync(_settings);
    }

    private void RecordRecentSource(string path)
    {
        _settings.RecentSources.Remove(path);
        _settings.RecentSources.Insert(0, path);
        if (_settings.RecentSources.Count > 10)
            _settings.RecentSources.RemoveAt(10);
        RefreshSourceBookmarks();
    }

    private void RefreshSourceBookmarks()
    {
        SourceBookmarks.Clear();
        foreach (var path in _settings.FavouritePaths.Concat(_settings.RecentSources).Distinct())
            SourceBookmarks.Add(path);
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
        // Load persisted settings; merge into existing _settings instance
        // (FilterChainViewModel holds a ref to the same instance).
        var saved = await _settingsStore.LoadAsync();
        _settings.RecentSources = saved.RecentSources;
        _settings.FavouritePaths = saved.FavouritePaths;
        if (saved.LastSourcePath is { Length: > 0 })
            SourcePath = saved.LastSourcePath;
        RefreshSourceBookmarks();

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
