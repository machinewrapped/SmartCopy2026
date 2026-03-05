using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Progress;
using SmartCopy.Core.Selection;
using SmartCopy.Core.Settings;
using SmartCopy.Core.Workflows;
using SmartCopy.UI.Services;
using SmartCopy.UI.ViewModels.Workflows;
using SmartCopy.UI.Views;
using SmartCopy.UI.Views.Workflows;

namespace SmartCopy.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const int MaxRecentSources = 10;    // TODO: make this configurable

    [ObservableProperty]
    private SourceBookmarkItem? _selectedSourceBookmark;

    [ObservableProperty]
    private bool _useAbsolutePathsForSelection;

    [ObservableProperty]
    private bool _autoOpenLogOnRun = true;

    [ObservableProperty]
    private bool _showExcludedNodesByDefault = true;

    [ObservableProperty]
    private bool _restoreLastWorkflow;

    [ObservableProperty]
    private bool _restoreLastSourcePath = true;

    [ObservableProperty]
    private bool _disableDestructivePreview;

    [ObservableProperty]
    private bool _deleteToRecycleBin = true;

    [ObservableProperty]
    private bool _saveSessionLocally;

    [ObservableProperty]
    private bool _fullPreScan;

    [ObservableProperty]
    private bool _lazyExpandScan;

    [ObservableProperty]
    private string _defaultOverwriteMode = "Skip";

    [ObservableProperty]
    private bool _addArtificialDelay = false;

    public string SourcePath
    {
        get => SourcePathPicker.Path;
        set => SourcePathPicker.Path = value;
    }

    public string SourcePathValidationMessage
    {
        get => SourcePathPicker.ValidationMessage;
        set => SourcePathPicker.ValidationMessage = value;
    }

    private readonly AppDataPaths _paths = AppDataPaths.ForCurrentUser();
    private readonly MemoryFileSystemProvider _memoryProvider;
    private readonly FileSystemProviderRegistry _providerRegistry = new();
    private readonly FilterContext _filterContext;
    private readonly AppSettings _settings;
    private readonly AppSettingsStore _settingsStore = new();
    private readonly SessionStore _sessionStore = new();
    private readonly OperationJournal _operationJournal;
    private readonly WorkflowPresetStore _workflowStore;
    private readonly SelectionManager _selectionManager = new();
    private readonly SelectionSerializer _selectionSerializer = new();
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _scanCts;
    private string _lastCommittedSourcePath = string.Empty;

    public PathPickerViewModel SourcePathPicker { get; }

    public ObservableCollection<SourceBookmarkItem> SourceBookmarks { get; } = [];

    public DirectoryTreeViewModel DirectoryTree { get; }
    public FileListViewModel FileList { get; }
    public FilterChainViewModel FilterChain { get; }
    public PipelineViewModel Pipeline { get; }
    public StatusBarViewModel StatusBar { get; } = new();
    public PreviewViewModel Preview { get; } = new();
    public WorkflowMenuViewModel WorkflowMenu { get; }
    public LogPanelViewModel LogPanel { get; } = new();

    public MainViewModel()
    {
        _settings = new AppSettings { SettingsFilePath = _paths.Settings };
        _operationJournal = new OperationJournal(_paths.Logs);
        _workflowStore = new WorkflowPresetStore(_paths.Workflows);

        var filterPresetStore = new FilterPresetStore(_paths.FilterPresets);

        // Create an in-memory virtual file system for testing.
        // TODO: this should be a debug option, not exposed in release builds
        _memoryProvider = MockMemoryFileSystemFactory.CreateSeeded(artificialDelay: _settings.AddArtificialDelay);
        _providerRegistry.Register(_memoryProvider);

        // Create the context and ViewModel for the filter chain
        _filterContext = new FilterContext(_providerRegistry);
        FilterChain = new FilterChainViewModel(
            filterPresetStore,
            _settings,
            new FilterChainPresetStore(_paths.FilterChains));

        // Create the pipeline view model
        Pipeline = new PipelineViewModel(
            presetStore: new PipelinePresetStore(_paths.Pipelines),
            stepPresetStore: new StepPresetStore(_paths.StepPresets),
            appSettings: _settings);

        // Create the source path picker
        SourcePathPicker = new PathPickerViewModel(_settings, _settingsStore, PathPickerMode.Source);

        // TODO: we will need to be able to init the viewmodel without a provider
        DirectoryTree = new DirectoryTreeViewModel(_providerRegistry)
        {
            ShowFilteredNodesInTree = _settings.ShowFilteredNodesInTree
        };

        FilterChain.VisibilityToggled += (_, isVisible) =>
        {
            DirectoryTree.ShowFilteredNodesInTree = isVisible;
        };

        DirectoryTree.SetAsSourcePathRequested += async (_, path) =>
        {
            await ApplySourcePathCoreAsync(path);
        };

        FileList = new FileListViewModel(_filterContext);

        WorkflowMenu = new WorkflowMenuViewModel(_workflowStore);
        WorkflowMenu.SaveRequested += async (_, _) => await SaveWorkflowAsync();
        WorkflowMenu.LoadRequested += async (_, name) => await LoadWorkflowAsync(name);
        WorkflowMenu.ManageRequested += async (_, _) => await ManageWorkflowsAsync();

        Pipeline.PipelineChanged += (_, _) =>
        {
            FilterChain.PipelineDestinationPath = Pipeline.FirstDestinationPath;
            WorkflowMenu.CanSave = Pipeline.Steps.Count > 0;
        };
        Pipeline.RunRequested += async (_, _) => await RunPipelineAsync();
        Pipeline.PreviewRequested += async (_, _) => await PreviewPipelineAsync();
        FilterChain.PipelineDestinationPath = Pipeline.FirstDestinationPath;

        // Subscribe to chain changes — re-evaluate filters within ~100 ms.
        FilterChain.ChainChanged += OnChainChanged;
        DirectoryTree.SelectionChanged += (_, _) => RefreshIdleStats();

        DirectoryTree.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(DirectoryTreeViewModel.IsLoading))
            {
                StatusBar.IsScanning = DirectoryTree.IsLoading;
                StatusBar.ScanStatusText = DirectoryTree.IsLoading ? "Scanning..." : string.Empty;
            }
            else if (e.PropertyName == nameof(DirectoryTreeViewModel.SelectedNode))
            {
                var selectedNode = DirectoryTree.SelectedNode;
                if (selectedNode?.IsDirectory == true)
                {
                    try
                    {
                        await FileList.LoadFilesForNodeAsync(selectedNode, FilterChain.BuildLiveChain());
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to load files for directory: {ex}");
                    }
                }
            }
        };

        SourcePathPicker.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PathPickerViewModel.Path))
            {
                OnPropertyChanged(nameof(SourcePath));
                OnPropertyChanged(nameof(SourcePathValidationMessage));
            }
        };

        SourcePathPicker.PathCommitted += async (s,p) => await ApplySourcePathCoreAsync(p);

        StatusBar.CancelScanRequested += (_, _) => _scanCts?.Cancel();

        InitializeInBackground();
    }

    partial void OnSelectedSourceBookmarkChanged(SourceBookmarkItem? value)
    {
        // Only populate the text field — don't apply until the user commits
        // (Enter key or dropdown close after mouse selection).
        if (value is not null)
        {
            SourcePath = value.Path;
        }
    }

    partial void OnUseAbsolutePathsForSelectionChanged(bool value)
    {
        _settings.UseAbsolutePathsForSelectionSave = value;
        _ = SaveSettingsAsync();
    }

    partial void OnAutoOpenLogOnRunChanged(bool value)
    {
        _settings.AutoOpenLogOnRun = value;
        _ = SaveSettingsAsync();
    }

    partial void OnShowExcludedNodesByDefaultChanged(bool value)
    {
        _settings.ShowFilteredNodesInTree = value;
        _ = SaveSettingsAsync();
        FilterChain.ShowExcludedNodesInTree = value;
    }

    partial void OnRestoreLastWorkflowChanged(bool value)
    {
        _settings.RestoreLastWorkflow = value;
        _ = SaveSettingsAsync();
    }

    partial void OnRestoreLastSourcePathChanged(bool value)
    {
        _settings.RestoreLastSourcePath = value;
        _ = SaveSettingsAsync();
    }

    partial void OnDisableDestructivePreviewChanged(bool value)
    {
        _settings.DisableDestructivePreview = value;
        _ = SaveSettingsAsync();
    }

    partial void OnDeleteToRecycleBinChanged(bool value)
    {
        _settings.DeleteToRecycleBin = value;
        _settings.DefaultDeleteMode = value ? "Trash" : "Permanent";
        _ = SaveSettingsAsync();
    }

    partial void OnSaveSessionLocallyChanged(bool value)
    {
        _settings.SaveSessionLocally = value;
        _ = SaveSettingsAsync();
    }

    partial void OnFullPreScanChanged(bool value)
    {
        _settings.FullPreScan = value;
        _ = SaveSettingsAsync();
    }

    partial void OnLazyExpandScanChanged(bool value)
    {
        _settings.LazyExpandScan = value;
        _ = SaveSettingsAsync();
    }

    partial void OnDefaultOverwriteModeChanged(string value)
    {
        _settings.DefaultOverwriteMode = value;
        _ = SaveSettingsAsync();
    }

    partial void OnAddArtificialDelayChanged(bool value)
    {
        _memoryProvider.AddArtificialDelay = value;
        _settings.AddArtificialDelay = value;
        _ = SaveSettingsAsync();
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Settings] Failed to save settings: {ex.Message}");
        }
    }

    // ── Keyboard shortcut commands ────────────────────────────────────────────

    [RelayCommand]
    private async Task Rescan()
    {
        var path = SourcePath.Trim();
        if (string.IsNullOrWhiteSpace(path)) return;
        await ApplySourcePathCoreAsync(path);
    }

    [RelayCommand]
    private void CancelOperation()
    {
        if (StatusBar.Progress.IsActive)
            StatusBar.Progress.CancelCommand.Execute(null);
    }


    // ── Selection commands ──────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectAll()
    {
        if (DirectoryTree.RootNode == null)
            return;

        _selectionManager.SelectAll([DirectoryTree.RootNode]);
        RefreshIdleStats();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        if (DirectoryTree.RootNode == null)
            return;

        _selectionManager.ClearAll([DirectoryTree.RootNode]);
        RefreshIdleStats();
    }

    [RelayCommand]
    private void InvertSelection()
    {
        if (DirectoryTree.RootNode == null)
            return;

        _selectionManager.InvertAll([DirectoryTree.RootNode]);
        RefreshIdleStats();
    }

    [RelayCommand]
    private async Task SaveSelectionAsText()
    {
        if (DirectoryTree.RootNode == null) return;

        var mainWindow = GetMainWindow();
        if (mainWindow is null) return;

        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Selection",
            SuggestedFileName = "selection",
            DefaultExtension = ".sc2sel",
            FileTypeChoices =
            [
                new FilePickerFileType("SmartCopy Selection") { Patterns = ["*.sc2sel"] },
                new FilePickerFileType("Text File")           { Patterns = ["*.txt"]    },
            ],
        });

        if (file is null) return;
        var path = file.Path.LocalPath;
        var snapshot = _selectionManager.Capture([DirectoryTree.RootNode], UseAbsolutePathsForSelection);
        await _selectionSerializer.SaveAsync(path, snapshot);
    }

    [RelayCommand]
    private async Task SaveSelectionAsPlaylist()
    {
        if (DirectoryTree.RootNode == null) return;

        var mainWindow = GetMainWindow();
        if (mainWindow is null) return;

        var file = await mainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Selection as Playlist",
            SuggestedFileName = "selection",
            DefaultExtension = ".m3u",
            FileTypeChoices =
            [
                new FilePickerFileType("M3U Playlist")          { Patterns = ["*.m3u"]  },
                new FilePickerFileType("M3U8 UTF-8 Playlist")   { Patterns = ["*.m3u8"] },
            ],
        });

        if (file is null) return;
        var path = file.Path.LocalPath;
        var snapshot = _selectionManager.Capture([DirectoryTree.RootNode], UseAbsolutePathsForSelection);
        await _selectionSerializer.SaveAsync(path, snapshot);
    }

    [RelayCommand]
    private async Task RestoreSelection()
    {
        if (DirectoryTree.RootNode == null) return;

        var mainWindow = GetMainWindow();
        if (mainWindow is null) return; 

        var files = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Restore Selection",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Selection Files") { Patterns = ["*.sc2sel", "*.txt", "*.m3u", "*.m3u8"] },
                new FilePickerFileType("All Files")       { Patterns = ["*.*"] },
            ],
        });

        if (files is not { Count: > 0 }) return;
        var path = files[0].Path.LocalPath;
        var snapshot = await _selectionSerializer.LoadAsync(path);
        var result = _selectionManager.Restore([DirectoryTree.RootNode], snapshot);
        RefreshIdleStats();

        if (result.HasUnmatched)
            Debug.WriteLine($"[Selection] Restored {result.MatchedCount} of {snapshot.Paths.Count}; "
                + $"{result.UnmatchedPaths.Count} unmatched: {string.Join(", ", result.UnmatchedPaths)}");
    }

    private static Window? GetMainWindow()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d
            ? d.MainWindow : null;

    private void RevertSourcePath()
    {
        SourcePath = _lastCommittedSourcePath;
    }

    private async Task ApplySourcePathCoreAsync(string path)
    {
        var normalizedPath = PathHelper.NormalizeUserPath(path);

        if (string.IsNullOrWhiteSpace(normalizedPath))
            return;

        // Check if the path has actually changed        
        var previousSourcePath = DirectoryTree.SourcePath;
        if (normalizedPath == previousSourcePath)
            return;

        // Cancel any in-progress scan and start a fresh token for this one.
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        try
        {
            SourcePath = normalizedPath;
            SourcePathValidationMessage = string.Empty;

            FileList.Clear();

            await DirectoryTree.ChangeRootAsync(normalizedPath, ct);

            _lastCommittedSourcePath = normalizedPath;

            RecordRecentSource(normalizedPath);

            // Apply filter chain
            await ApplyFiltersAsync();

            _settings.LastSourcePath = normalizedPath;
            await _settingsStore.SaveAsync(_settings);
        }
        catch (OperationCanceledException)
        {
            // Scan was cancelled (new path committed or user pressed Cancel).+
            SourcePathValidationMessage = "Directory scan was cancelled before it was complete";
            LogPanel.AddEntry(SourcePathValidationMessage, LogLevel.Warning);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to change source path to '{normalizedPath}': {ex}");

            SourcePathValidationMessage = BuildSourcePathValidationMessage(normalizedPath, ex);
            LogPanel.AddEntry(SourcePathValidationMessage, LogLevel.Error);

            // Revert path display on failure; do not attempt to re-scan the previous path.
            SourcePath = previousSourcePath ?? string.Empty;
        }
    }

    [RelayCommand]
    private async Task BookmarkCurrentPath()
    {
        var path = PathHelper.NormalizeUserPath(SourcePath);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (_settings.FavouritePaths.Any(existing => PathHelper.AreEquivalentUserPaths(existing, path)))
        {
            return;
        }

        _settings.FavouritePaths.Insert(0, path);
        RefreshSourceBookmarks();
        await _settingsStore.SaveAsync(_settings);
    }

    [RelayCommand]
    private async Task RemoveSourceBookmark(SourceBookmarkItem? item)
    {
        if (item is null) return;

        var normalizedPath = PathHelper.NormalizeUserPath(item.Path);
        bool removed;
        if (item.IsBookmark)
        {
            removed = RemoveEquivalentPath(_settings.FavouritePaths, normalizedPath);
        }
        else
        {
            removed = RemoveEquivalentPath(_settings.RecentSources, normalizedPath);
        }

        if (removed)
        {
            RefreshSourceBookmarks();
            await _settingsStore.SaveAsync(_settings);
        }
    }

    private void RecordRecentSource(string path)
    {
        var normalizedPath = PathHelper.NormalizeUserPath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return;
        }

        RemoveEquivalentPath(_settings.RecentSources, normalizedPath);
        _settings.RecentSources.Insert(0, normalizedPath);
        if (_settings.RecentSources.Count > MaxRecentSources)
        {
            _settings.RecentSources.RemoveAt(MaxRecentSources);
        }

        RefreshSourceBookmarks();
    }

    private void RefreshSourceBookmarks()
    {
        // Preserve the current text — Clear() nulls SelectedItem which
        // causes the editable ComboBox to wipe its Text binding.
        var currentPath = PathHelper.NormalizeUserPath(SourcePath);

        SourceBookmarks.Clear();
        var addedPaths = new HashSet<string>(PathHelper.PathComparer);

        var normalizedFavourites = PathHelper.NormalizeDistinctUserPaths(_settings.FavouritePaths);
        var normalizedRecent = PathHelper.NormalizeDistinctUserPaths(_settings.RecentSources);

        // Save normalized lists back
        _settings.FavouritePaths = normalizedFavourites;
        _settings.RecentSources = normalizedRecent;

        foreach (var path in _settings.FavouritePaths)
        {
            if (addedPaths.Add(path))
                SourceBookmarks.Add(new SourceBookmarkItem(path, true));
        }

        foreach (var path in _settings.RecentSources)
        {
            if (addedPaths.Add(path))
                SourceBookmarks.Add(new SourceBookmarkItem(path, false));
        }

        SourcePath = currentPath;
    }

    private static bool RemoveEquivalentPath(List<string> list, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var normalizedPath = PathHelper.NormalizeUserPath(path);
        return list.RemoveAll(existing => PathHelper.AreEquivalentUserPaths(existing, normalizedPath)) > 0;
    }

    private IFileSystemProvider ResolveSourceProvider(string normalizedPath) =>
        _providerRegistry.Resolve(normalizedPath)
        ?? throw new NotSupportedException($"No provider for path: {normalizedPath}");

    private static string BuildSourcePathValidationMessage(string path, Exception ex)
    {
        return ex switch
        {
            DirectoryNotFoundException => $"Path not found: {path}",
            FileNotFoundException => $"Path not found: {path}",
            UnauthorizedAccessException => $"Access denied: {path}",
            ArgumentException => $"Invalid path: {path}",
            NotSupportedException => $"Unsupported path format: {path}",
            _ => $"Could not open source path: {path}",
        };
    }

    private async void OnChainChanged(object? sender, EventArgs e)
    {
        if (DirectoryTree == null || DirectoryTree.IsLoaded == false || DirectoryTree.IsLoading)
        {
            return;
        }

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

        await FileList.ApplyChainToFilesAsync(chain, ct);
        await DirectoryTree.ApplyFiltersAsync(chain, _filterContext, ct);

        RefreshIdleStats();
    }

    private void RefreshIdleStats()
    {
        if (DirectoryTree.RootNode == null)
        {
            Pipeline.SetSelectedIncludedFileCount(0);
            StatusBar.Selection.UpdateStats(0,0,0);
            return;
        }

        var root = DirectoryTree.RootNode;
        root.BuildStats();

        int selected = root.NumSelectedFiles;
        int filteredOut = root.NumFilterExcludedFiles;
        long totalBytes = root.TotalSelectedBytes;

        Pipeline.SetSelectedIncludedFileCount(selected);
        StatusBar.Selection.UpdateStats(selected, totalBytes, filteredOut);
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
        var saved = await _settingsStore.LoadAsync(_settings.SettingsFilePath!);
        _settings.RecentSources = saved.RecentSources;
        _settings.FavouritePaths = saved.FavouritePaths;
        _settings.LastSourcePath = saved.LastSourcePath;
        _settings.LogRetentionDays = saved.LogRetentionDays;
        _settings.UseAbsolutePathsForSelectionSave = saved.UseAbsolutePathsForSelectionSave;
        _settings.AutoOpenLogOnRun = saved.AutoOpenLogOnRun;
        _settings.ShowFilteredNodesInTree = saved.ShowFilteredNodesInTree;
        _settings.RestoreLastWorkflow = saved.RestoreLastWorkflow;
        _settings.RestoreLastSourcePath = saved.RestoreLastSourcePath;
        _settings.DisableDestructivePreview = saved.DisableDestructivePreview;
        _settings.DeleteToRecycleBin = saved.DeleteToRecycleBin;
        _settings.DefaultDeleteMode = saved.DeleteToRecycleBin ? "Trash" : "Permanent";
        _settings.FullPreScan = saved.FullPreScan;
        _settings.LazyExpandScan = saved.LazyExpandScan;
        _settings.DefaultOverwriteMode = saved.DefaultOverwriteMode;

        _settings.SaveSessionLocally = saved.SaveSessionLocally;

        UseAbsolutePathsForSelection = _settings.UseAbsolutePathsForSelectionSave;
        AutoOpenLogOnRun = _settings.AutoOpenLogOnRun;
        ShowExcludedNodesByDefault = _settings.ShowFilteredNodesInTree;
        RestoreLastWorkflow = _settings.RestoreLastWorkflow;
        RestoreLastSourcePath = _settings.RestoreLastSourcePath;
        DisableDestructivePreview = _settings.DisableDestructivePreview;
        DeleteToRecycleBin = _settings.DeleteToRecycleBin;
        SaveSessionLocally = _settings.SaveSessionLocally;
        FullPreScan = _settings.FullPreScan;
        LazyExpandScan = _settings.LazyExpandScan;
        DefaultOverwriteMode = _settings.DefaultOverwriteMode;

        SourcePathPicker.RefreshSettings();

        await _operationJournal.RotateAsync(_settings.LogRetentionDays);
        await WorkflowMenu.RefreshAsync();

        // Restore last workflow if the option is enabled and a session snapshot exists.
        if (saved.RestoreLastWorkflow)
        {
            try
            {
                var session = await _sessionStore.LoadAsync(GetSessionPath());
                if (session is not null)
                {
                    ApplyWorkflowConfig(session);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                Debug.WriteLine($"Failed to restore session snapshot: {ex}");
            }
        }
        else if (saved.RestoreLastSourcePath && saved.LastSourcePath is { Length: > 0 })
        {
            SourcePath = saved.LastSourcePath;
        }
        else
        {
            // TEMP: set a safe default path
            SourcePath = MockMemoryFileSystemFactory.SourcePath;
        }

        RefreshSourceBookmarks();

        if (SourcePath is { Length: > 0 })
        {
            try
            {
                await ApplySourcePathCoreAsync(SourcePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set initial source path to '{SourcePath}': {ex}");
                RevertSourcePath();
            }
        }

        LogPanel.AddEntry("SmartCopy 2026 ready");
    }

    private async Task PreviewPipelineAsync()
    {
        var pipeline = Pipeline.BuildLivePipeline();
        if (DirectoryTree.IsLoaded == false)
            return;

        if (!Pipeline.CanRun)
            return;

        var rootNode = DirectoryTree.RootNode
            ?? throw new ApplicationException("Directory tree is loaded but root node is not set");

        var sourceProvider = DirectoryTree.SourceProvider
            ?? throw new ApplicationException("Directory tree is loaded but source provider is unknown");

        var runner = new PipelineRunner(pipeline);
        var job = new PipelineJob
        {
            RootNode         = rootNode,
            SourceProvider   = sourceProvider,
            ProviderRegistry = _providerRegistry,
            OverwriteMode    = ParseOverwriteMode(_settings.DefaultOverwriteMode),
            DeleteMode       = ParseDeleteMode(_settings.DefaultDeleteMode),
        };

        var plan = await runner.PreviewAsync(job, CancellationToken.None);

        Preview.LoadFrom(plan, pipeline.HasDeleteStep, GetDeleteModeFromPipeline(pipeline, job.DeleteMode));

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow is { } mainWindow)
        {
            var dialog = new PreviewView { DataContext = Preview };
            var confirmRun = await dialog.ShowDialog<bool?>(mainWindow);
            if (confirmRun == true)
            {
                await ExecutePipelineAsync(runner, job);
            }
        }
    }

    private async Task RunPipelineAsync()
    {
        var pipeline = Pipeline.BuildLivePipeline();

        // Mandatory preview for destructive pipelines, unless the user has opted out.
        if (pipeline.HasDeleteStep && !_settings.DisableDestructivePreview)
        {
            await PreviewPipelineAsync();
            return;
        }

        if (DirectoryTree.IsLoaded == false)
            return;

        var rootNode = DirectoryTree.RootNode
            ?? throw new ApplicationException("Directory tree is loaded but root node is not set");

        Pipeline.SetSelectedIncludedFileCount(rootNode.NumSelectedFiles);

        if (!Pipeline.CanRun)
            return;

        var sourceProvider = DirectoryTree.SourceProvider
            ?? throw new ApplicationException("Directory tree is loaded but source provider is unknown");

        var runner = new PipelineRunner(pipeline);
        var job = new PipelineJob
        {
            RootNode         = rootNode,
            SourceProvider   = sourceProvider,
            ProviderRegistry = _providerRegistry,
            OverwriteMode    = ParseOverwriteMode(_settings.DefaultOverwriteMode),
            DeleteMode       = ParseDeleteMode(_settings.DefaultDeleteMode),
        };
        await ExecutePipelineAsync(runner, job);
    }

    private async Task ExecutePipelineAsync(PipelineRunner runner, PipelineJob job)
    {
        var nodeProgress = new Progress<TransformResult>(OnNodeCompleted);
        var executionJob = StatusBar.Progress.Begin(job with { NodeProgress = nodeProgress });

        Pipeline.IsRunning = true;

        if (AutoOpenLogOnRun)
            LogPanel.IsExpanded = true;

        try
        {
            var results = await runner.ExecuteAsync(executionJob);

            await _operationJournal.WriteAsync(results.Where(r => r.SourceNodeResult != SourceResult.None));

            foreach (var r in results)
            {
                if (!r.IsSuccess)
                {
                    LogPanel.AddEntry($"Failed: {r.SourceNode.Name}", LogLevel.Error);
                }
                else if (r.SourceNodeResult == SourceResult.Copied)
                {
                    LogPanel.AddEntry($"Copied {r.SourceNode.Name} → {r.DestinationPath} ({FileSizeFormatter.FormatBytes(r.OutputBytes)})");
                }
                else if (r.SourceNodeResult == SourceResult.Moved)
                {
                    LogPanel.AddEntry($"Moved {r.SourceNode.Name} → {r.DestinationPath} ({FileSizeFormatter.FormatBytes(r.OutputBytes)})");
                }
                else if (r.SourceNodeResult is SourceResult.Trashed or SourceResult.Deleted)
                {
                    LogPanel.AddEntry($"Deleted {r.SourceNode.Name} ({FileSizeFormatter.FormatBytes(r.InputBytes)})");
                }

                if (r.DestinationResult != DestinationResult.None)
                {
                    if (r.DestinationResult == DestinationResult.Overwritten)
                    {
                        LogPanel.AddEntry($"Overwrote {r.DestinationPath ?? "(unknown)"}");
                    }
                }
            }

            StatusBar.Progress.Complete();
        }
        catch (OperationCanceledException)
        {
            StatusBar.Progress.Cancelled();
        }
        finally
        {
            Pipeline.IsRunning = false;
            FileList.RemoveAllMarkedForRemoval();
            DirectoryTree.RemoveNodesMarkedForRemoval();
        }
    }

    private void OnNodeCompleted(TransformResult result)
    {
        if (!result.IsSuccess)
            return;

        if (result.SourceNodeResult is SourceResult.Moved or SourceResult.Trashed or SourceResult.Deleted)
        {
            result.SourceNode.MarkForRemoval();
        }
    }

    private async Task SaveWorkflowAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } mainWindow)
        {
            return;
        }

        var vm = new SaveWorkflowDialogViewModel();
        foreach (var preset in WorkflowMenu.SavedWorkflows)
        {
            vm.ExistingNames.Add(preset.Name);
        }

        var dialog = new SaveWorkflowDialog { DataContext = vm };
        var result = await dialog.ShowDialog<bool?>(mainWindow);
        if (result != true || string.IsNullOrWhiteSpace(vm.WorkflowName))
        {
            return;
        }

        var name = vm.WorkflowName.Trim();
        var filterChainConfig = FilterChain.BuildLiveChain().ToConfig(name);
        var pipelineConfig = Pipeline.ToConfig(name);
        var workflowConfig = new WorkflowConfig(
            Name: name,
            Description: null,
            SourcePath: SourcePath,
            FilterChain: filterChainConfig,
            Pipeline: pipelineConfig);

        await _workflowStore.SaveUserPresetAsync(name, workflowConfig);
        await WorkflowMenu.RefreshAsync();
    }

    private async Task LoadWorkflowAsync(string name)
    {
        var preset = WorkflowMenu.SavedWorkflows.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        if (preset is null)
        {
            Debug.WriteLine($"Failed to find workflow preset with name '{name}'.");
            return;
        }

        ApplyWorkflowConfig(preset.Config);

        await ApplySourcePathCoreAsync(SourcePath);
    }

    /// <summary>
    /// Applies a <see cref="WorkflowConfig"/> to the current session without
    /// triggering a directory scan — just restores source, filters, and pipeline.
    /// </summary>
    private void ApplyWorkflowConfig(WorkflowConfig config)
    {
        DirectoryTree.Reset();

        // Restore source path
        SourcePath = config.SourcePath;

        // Restore filter chain
        FilterChain.Filters.Clear();
        foreach (var filterConfig in config.FilterChain.Filters)
        {
            var filter = FilterFactory.FromConfig(filterConfig);
            FilterChain.AddFilterFromResult(filter);
        }

        // Restore pipeline
        var pipelinePreset = new PipelinePreset
        {
            Id = "workflow",
            Name = config.Name,
            IsBuiltIn = false,
            Config = config.Pipeline,
        };
        Pipeline.LoadPreset(pipelinePreset);
    }

    /// <summary>
    /// Captures the full current session state and writes it to the session snapshot
    /// file so it can be restored on the next startup (when RestoreLastWorkflow is on).
    /// Called from MainWindow.OnClosing — best-effort, never throws.
    /// </summary>
    public async Task SaveSessionSnapshotAsync()
    {
        try
        {
            var filterChainConfig = FilterChain.BuildLiveChain().ToConfig("__session__");
            var pipelineConfig = Pipeline.ToConfig("__session__");
            var config = new WorkflowConfig(
                Name: "__session__",
                Description: null,
                SourcePath: SourcePath,
                FilterChain: filterChainConfig,
                Pipeline: pipelineConfig);
            await _sessionStore.SaveAsync(config, GetSessionPath());
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save session snapshot: {ex}");
        }
    }

    /// <summary>
    /// Returns the path for session.sc2session: next to the executable when
    /// <see cref="SaveSessionLocally"/> is on, otherwise the global settings directory.
    /// </summary>
    private string GetSessionPath()
        => _settings.SaveSessionLocally
            ? Path.Combine(AppContext.BaseDirectory, "session.sc2session")
            : _paths.Session;

    private async Task ManageWorkflowsAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } mainWindow)
        {
            return;
        }

        var vm = new ManageWorkflowsDialogViewModel(_workflowStore);
        await vm.LoadAsync();

        var dialog = new ManageWorkflowsDialog { DataContext = vm };
        await dialog.ShowDialog(mainWindow);

        if (vm.HasChanges)
        {
            await WorkflowMenu.RefreshAsync();
        }

        if (!string.IsNullOrEmpty(vm.LoadRequestedWorkflowName))
        {
            await LoadWorkflowAsync(vm.LoadRequestedWorkflowName);
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
