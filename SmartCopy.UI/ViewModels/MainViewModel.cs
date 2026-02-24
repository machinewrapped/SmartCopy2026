using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Pipeline.Validation;
using SmartCopy.Core.Progress;
using SmartCopy.Core.Settings;
using SmartCopy.UI.Services;
using SmartCopy.UI.Views;

namespace SmartCopy.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const int MaxRecentSources = 10;    // TODO: make this configurable

    [ObservableProperty]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private string? _selectedSourceBookmark;

    private readonly MemoryFileSystemProvider _memoryProvider;
    private readonly AppSettings _settings = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly OperationJournal _operationJournal = new();
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _runCts;

    public ObservableCollection<string> SourceBookmarks { get; } = [];

    public DirectoryTreeViewModel DirectoryTree { get; }
    public FileListViewModel FileList { get; }
    public FilterChainViewModel FilterChain { get; }
    public PipelineViewModel Pipeline { get; }
    public StatusBarViewModel StatusBar { get; } = new();
    public PreviewViewModel Preview { get; } = new();

    public MainViewModel()
    {
        var presetStore = new FilterPresetStore();

        _memoryProvider = MockMemoryFileSystemFactory.CreateSeeded();
        _memoryProvider.SeedDirectory(MockMemoryFileSystemFactory.TargetPath);
        SourcePath = MockMemoryFileSystemFactory.SourcePath;

        FilterChain = new FilterChainViewModel(presetStore, _settings);
        Pipeline = new PipelineViewModel(
            presetStore: new PipelinePresetStore());

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

        Pipeline.PipelineChanged += (_, _) => FilterChain.PipelineDestinationPath = Pipeline.FirstDestinationPath;
        Pipeline.RunRequested += async (_, _) => await RunPipelineAsync();
        Pipeline.PreviewRequested += async (_, _) => await PreviewPipelineAsync();
        FilterChain.PipelineDestinationPath = Pipeline.FirstDestinationPath;

        // Subscribe to chain changes — re-evaluate filters within ~100 ms.
        FilterChain.ChainChanged += OnChainChanged;
        DirectoryTree.SelectionChanged += (_, _) => RefreshIdleStats();

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
        if (_settings.RecentSources.Count > MaxRecentSources)
        {
            _settings.RecentSources.RemoveAt(MaxRecentSources);
        }

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
        RefreshIdleStats();
    }

    private void RefreshIdleStats()
    {
        int selected = 0;
        long totalBytes = 0;
        int filteredOut = 0;

        foreach (var root in DirectoryTree.RootNodes)
            CollectStatsRecursive(root, ref selected, ref totalBytes, ref filteredOut);

        Pipeline.SetSelectedIncludedFileCount(selected);
        StatusBar.Selection.UpdateStats(selected, totalBytes, filteredOut);
    }

    private static void CollectStatsRecursive(FileSystemNode node, ref int selected, ref long totalBytes, ref int filteredOut)
    {
        foreach (var file in node.Files)
        {
            if (file.IsSelected)
            {
                selected++;
                totalBytes += file.Size;
            }
            else if (file.CheckState == CheckState.Checked && file.FilterResult == FilterResult.Excluded)
            {
                filteredOut++;
            }
        }

        foreach (var child in node.Children)
            CollectStatsRecursive(child, ref selected, ref totalBytes, ref filteredOut);
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
        _settings.LastSourcePath = saved.LastSourcePath;
        _settings.LogRetentionDays = saved.LogRetentionDays;

        if (saved.LastSourcePath is { Length: > 0 })
        {
            SourcePath = saved.LastSourcePath;
        }
        RefreshSourceBookmarks();

        // Phase 1: hardcode /mem/Mirror as the mirror-filter comparison path.
        FilterChain.PipelineDestinationPath = MockMemoryFileSystemFactory.TargetPath;
        await _operationJournal.RotateAsync(_settings.LogRetentionDays);

        // Pre-wire the chain before the initial tree load so the first file list load
        // already has a chain to evaluate.
        var chain = FilterChain.BuildLiveChain();
        FileList.UpdateChain(chain, _memoryProvider);

        await DirectoryTree.InitializeAsync(MockMemoryFileSystemFactory.DefaultFileListPath);

        // TODO: automatically reading the last used directory on startup could be expensive,
        // especially if it was a network drive or MTP. It should definitely be a setting that can be turned off.
        // However, for Phase 1 when the filesystem is in-memory it is convenient for validating the UI/UX.
        if (SourcePath is { Length: > 0 })
        {
            try
            {
                await DirectoryTree.ChangeRootAsync(SourcePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set initial source path to '{SourcePath}': {ex}");
                SourcePath = DirectoryTree.RootNodes.FirstOrDefault()?.FullPath ?? SourcePath;
            }
        }

        if (DirectoryTree.SelectedNode != null)
        {
            await FileList.LoadFilesForNodeAsync(DirectoryTree.SelectedNode);
        }

        // Apply filters to the freshly loaded tree.
        await ApplyFiltersAsync();
    }

    private async Task PreviewPipelineAsync()
    {
        var selectedFiles = CollectSelectedFiles();
        Pipeline.SetSelectedIncludedFileCount(selectedFiles.Count);

        if (!Pipeline.CanRun)
        {
            return;
        }

        if (selectedFiles.Count == 0)
        {
            return;
        }

        var pipeline = Pipeline.BuildLivePipeline();
        var runner = new PipelineRunner(pipeline);
        var overwriteMode = ParseOverwriteMode(_settings.DefaultOverwriteMode);
        var deleteMode = ParseDeleteMode(_settings.DefaultDeleteMode);

        var plan = await runner.PreviewAsync(
            selectedFiles,
            _memoryProvider,
            _memoryProvider,
            overwriteMode,
            deleteMode,
            CancellationToken.None);

        Preview.LoadFrom(plan, pipeline.HasDeleteStep, GetDeleteModeFromPipeline(pipeline, deleteMode));

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } mainWindow)
        {
            var dialog = new PreviewView { DataContext = Preview };
            var confirmRun = await dialog.ShowDialog<bool?>(mainWindow);
            if (confirmRun == true)
            {
                await ExecutePipelineAsync(runner, selectedFiles, overwriteMode, deleteMode);
            }
        }
    }

    private async Task RunPipelineAsync()
    {
        var selectedFiles = CollectSelectedFiles();
        Pipeline.SetSelectedIncludedFileCount(selectedFiles.Count);

        if (!Pipeline.CanRun)
        {
            return;
        }

        var pipeline = Pipeline.BuildLivePipeline();
        if (pipeline.HasDeleteStep)
        {
            await PreviewPipelineAsync();
            return;
        }

        if (selectedFiles.Count == 0)
        {
            return;
        }

        var overwriteMode = ParseOverwriteMode(_settings.DefaultOverwriteMode);
        var deleteMode = ParseDeleteMode(_settings.DefaultDeleteMode);
        var runner = new PipelineRunner(pipeline);
        await ExecutePipelineAsync(runner, selectedFiles, overwriteMode, deleteMode);
    }

    private async Task ExecutePipelineAsync(
        PipelineRunner runner,
        IReadOnlyList<FileSystemNode> selectedFiles,
        OverwriteMode overwriteMode,
        DeleteMode deleteMode)
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();

        StatusBar.Progress.Begin(_runCts);
        var progress = new Progress<OperationProgress>(StatusBar.Progress.Update);

        try
        {
            var results = await runner.ExecuteAsync(
                selectedFiles,
                _memoryProvider,
                _memoryProvider,
                overwriteMode,
                deleteMode,
                progress,
                _runCts.Token);

            await _operationJournal.WriteAsync(results.Where(r => r.StepType is StepKind.Copy or StepKind.Move or StepKind.Delete));
            StatusBar.Progress.Complete();
        }
        catch (OperationCanceledException)
        {
            StatusBar.Progress.Cancelled();
        }
    }

    private IReadOnlyList<FileSystemNode> CollectSelectedFiles()
    {
        var selected = new List<FileSystemNode>();

        foreach (var root in DirectoryTree.RootNodes)
        {
            CollectSelectedFilesRecursive(root, selected);
        }

        return selected;
    }

    private static void CollectSelectedFilesRecursive(FileSystemNode node, List<FileSystemNode> output)
    {
        foreach (var file in node.Files)
        {
            if (file.IsSelected)
            {
                output.Add(file);
            }
        }

        foreach (var child in node.Children)
        {
            CollectSelectedFilesRecursive(child, output);
        }
    }

    private static OverwriteMode ParseOverwriteMode(string raw)
    {
        return Enum.TryParse<OverwriteMode>(raw, out var mode)
            ? mode
            : OverwriteMode.IfNewer;
    }

    private static DeleteMode ParseDeleteMode(string raw)
    {
        return Enum.TryParse<DeleteMode>(raw, out var mode)
            ? mode
            : DeleteMode.Trash;
    }

    private static DeleteMode GetDeleteModeFromPipeline(TransformPipeline pipeline, DeleteMode fallback)
    {
        var deleteStep = pipeline.Steps.OfType<DeleteStep>().FirstOrDefault();
        return deleteStep?.Mode ?? fallback;
    }
}
