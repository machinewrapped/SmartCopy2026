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
using SmartCopy.Core.Scanning;
using SmartCopy.Core.Selection;
using SmartCopy.Core.Settings;
using SmartCopy.Core.Trash;
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
    private bool _allowDeleteWithoutPreview;

    [ObservableProperty]
    private bool _allowOverwriteWithoutPreview;

    [ObservableProperty]
    private bool _deleteToRecycleBin = true;

    [ObservableProperty]
    private bool _saveSessionLocally;

    [ObservableProperty]
    private bool _fullPreScan;

    [ObservableProperty]
    private bool _lazyExpandScan;

    [ObservableProperty]
    private bool _followSymlinks;

    [ObservableProperty]
    private OverwriteMode _defaultOverwriteMode = OverwriteMode.Skip;

    [ObservableProperty]
    private DeleteMode _defaultDeleteMode = DeleteMode.Trash;

    [ObservableProperty]
    private bool _showHiddenFiles;

    [ObservableProperty]
    private bool _allowDeleteReadOnly;

#if DEBUG
    [ObservableProperty]
    private bool _enableMemoryFileSystem = false;

    [ObservableProperty]
    private bool _addArtificialDelay = false;

    [ObservableProperty]
    private bool _limitMemoryFileSystemCapacity = false;
#endif

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

    private readonly SmartCopyAppContext _appContext;
    private readonly ITrashService _trashService;
    private readonly FileSystemProviderRegistry _providerRegistry = new();
    private readonly LocalDirectoryWatcherFactory _watcherFactory = new();
    private readonly AppSettings _settings;
    private readonly AppSettingsStore _settingsStore = new();
    private readonly SessionStore _sessionStore = new();
    private readonly OperationJournal _operationJournal;
    private readonly WorkflowPresetStore _workflowStore;
    private readonly SelectionManager _selectionManager = new();
    private readonly SelectionSerializer _selectionSerializer = new();
    private readonly SemaphoreSlim _watcherApplyGate = new(1, 1);
#if DEBUG
    private MemoryFileSystemProvider? _memoryProvider;
#endif
    private IDirectoryWatcher? _directoryWatcher;
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
        var dataStore = LocalAppDataStore.ForCurrentUser();
        _settings = new AppSettings { SettingsFilePath = dataStore.GetFilePath("settings.json") };
        _appContext = new SmartCopyAppContext(_settings, dataStore, _providerRegistry);

        _trashService = CreateTrashService();

        _operationJournal = new OperationJournal(dataStore.GetDirectoryPath("Logs"));
        _workflowStore = new WorkflowPresetStore(dataStore.GetDirectoryPath("Workflows"));

        // Create the ViewModel for the filter chain
        FilterChain = new FilterChainViewModel(_appContext);

        // Create the pipeline view model
        Pipeline = new PipelineViewModel(_appContext);

        // Create the source path picker
        SourcePathPicker = new PathPickerViewModel(_settings, PathPickerMode.Source);

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

        FileList = new FileListViewModel();

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
        Pipeline.SwapSourceRequested += async (_, step) => await SwapSourceWithDestinationAsync(step);
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
                Pipeline.IsScanning = DirectoryTree.IsLoading;
            }
            else if (e.PropertyName == nameof(DirectoryTreeViewModel.SelectedNode))
            {
                var selectedNode = DirectoryTree.SelectedNode;
                if (selectedNode?.IsDirectory == true)
                {
                    try
                    {
                        await FileList.LoadFilesForNodeAsync(selectedNode, FilterChain.BuildLiveChain(), _appContext);
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

        SourcePathPicker.PathCommitted += async (s, p) =>
        {
            if (Pipeline.IsRunning)
            {
                var confirmed = await ConfirmCancelPipelineForSourceChangeAsync();
                if (!confirmed) return;
                StatusBar.Progress.CancelCommand.Execute(null);
            }
            await ApplySourcePathCoreAsync(p);
        };

        StatusBar.CancelScanRequested += (_, _) => _scanCts?.Cancel();

        InitializeInBackground();
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
        // Load persisted settings into the existing instance so that ViewModels holding a
        // reference to _settings see the updated values without object replacement.
        await _settingsStore.LoadIntoAsync(_settings);

        UseAbsolutePathsForSelection = _settings.UseAbsolutePathsForSelectionSave;
        AutoOpenLogOnRun = _settings.AutoOpenLogOnRun;
        ShowExcludedNodesByDefault = _settings.ShowFilteredNodesInTree;
        RestoreLastWorkflow = _settings.RestoreLastWorkflow;
        RestoreLastSourcePath = _settings.RestoreLastSourcePath;
        AllowDeleteWithoutPreview = _settings.AllowDeleteWithoutPreview;
        AllowOverwriteWithoutPreview = _settings.AllowOverwriteWithoutPreview;
        SaveSessionLocally = _settings.SaveSessionLocally;
        FullPreScan = _settings.FullPreScan;
        LazyExpandScan = _settings.LazyExpandScan;
        FollowSymlinks = _settings.FollowSymlinks;
        DefaultOverwriteMode = _settings.DefaultOverwriteMode;
        DefaultDeleteMode = _settings.DefaultDeleteMode;
        ShowHiddenFiles = _settings.ShowHiddenFiles;
        AllowDeleteReadOnly = _settings.AllowDeleteReadOnly;
#if DEBUG
        EnableMemoryFileSystem = _settings.EnableMemoryFileSystem;
        AddArtificialDelay = _settings.AddArtificialDelay;
        LimitMemoryFileSystemCapacity = _settings.LimitMemoryFileSystemCapacity;

        if (EnableMemoryFileSystem)
        {
            SetupMemoryFileSystem();
        }
#endif

        SourcePathPicker.RefreshSettings();

        await _operationJournal.RotateAsync(_settings.LogRetentionDays);
        await WorkflowMenu.RefreshAsync();

        // Restore last workflow if the option is enabled and a session snapshot exists.
        if (_settings.RestoreLastWorkflow)
        {
            try
            {
                var session = await _sessionStore.LoadAsync(GetSessionPath());
                if (session is not null)
                {
                    await ApplyWorkflowConfig(session);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                Debug.WriteLine($"Failed to restore session snapshot: {ex}");
            }
        }
        else if (_settings.RestoreLastSourcePath && _settings.LastSourcePath is { Length: > 0 })
        {
            SourcePath = _settings.LastSourcePath;
        }
        else
#if DEBUG
            // TEMP: set a safe default path
            SourcePath = MockMemoryFileSystemFactory.SourcePath;
#else
            SourcePath = string.Empty;
#endif

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

    partial void OnAllowDeleteWithoutPreviewChanged(bool value)
    {
        _settings.AllowDeleteWithoutPreview = value;
        _ = SaveSettingsAsync();
    }

    partial void OnAllowOverwriteWithoutPreviewChanged(bool value)
    {
        _settings.AllowOverwriteWithoutPreview = value;
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

    partial void OnFollowSymlinksChanged(bool value)
    {
        _settings.FollowSymlinks = value;
        _ = SaveSettingsAsync();
    }

    private ScanOptions BuildScanOptions() => new()
    {
        LazyExpand = LazyExpandScan,
        IncludeHidden = ShowHiddenFiles,
        FollowSymlinks = FollowSymlinks,
    };

    partial void OnDefaultOverwriteModeChanged(OverwriteMode value)
    {
        _settings.DefaultOverwriteMode = value;
        _ = SaveSettingsAsync();
    }

    partial void OnDefaultDeleteModeChanged(DeleteMode value)
    {
        _settings.DefaultDeleteMode = value;
        _ = SaveSettingsAsync();
    }

    partial void OnShowHiddenFilesChanged(bool value)
    {
        _settings.ShowHiddenFiles = value;
        _ = SaveSettingsAsync();
        // Since hidden files visibility impacts the tree, re-scan
        var path = SourcePath.Trim();
        if (!string.IsNullOrWhiteSpace(path))
        {
            _ = ApplySourcePathCoreAsync(path);
        }
    }

    partial void OnAllowDeleteReadOnlyChanged(bool value)
    {
        _settings.AllowDeleteReadOnly = value;
        _ = SaveSettingsAsync();
    }

#if DEBUG
    partial void OnEnableMemoryFileSystemChanged(bool value)
    {
        _settings.EnableMemoryFileSystem = value;
        _ = SaveSettingsAsync();

        if (value)
        {
            SetupMemoryFileSystem();
        }
        else if (_memoryProvider != null)
        {
            _providerRegistry.Unregister(_memoryProvider);
            _memoryProvider = null;
        }
    }

    partial void OnAddArtificialDelayChanged(bool value)
    {
        if (_memoryProvider != null)
        {
            _memoryProvider.AddArtificialDelay = value;            
        }
        _settings.AddArtificialDelay = value;
        _ = SaveSettingsAsync();
    }

    partial void OnLimitMemoryFileSystemCapacityChanged(bool value)
    {
        if (_memoryProvider != null)
        {
            _memoryProvider.SimulatedCapacity = value ? 100_000_000_000 : null;
        }
        _settings.LimitMemoryFileSystemCapacity = value;
        _ = SaveSettingsAsync();
    }
#endif

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

    private static async Task<bool> ConfirmCancelPipelineForSourceChangeAsync()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow is null) return false;

        var confirmVm = new ConfirmDialogViewModel
        {
            Title = "Pipeline Running",
            Message = "Abort the current operation and change the source path?",
            ConfirmText = "Abort & Change",
            CancelText = "Keep Running",
        };
        var dialog = new ConfirmDialog { DataContext = confirmVm };
        var result = await dialog.ShowDialog<bool?>(mainWindow);
        return result == true;
    }

    private async Task SwapSourceWithDestinationAsync(PipelineStepViewModel step)
    {
        if (step.Step is not IHasDestinationPath dest || !dest.HasDestinationPath)
            return;

        var oldSource = SourcePath;
        var oldDestination = dest.DestinationPath!;

        IPipelineStep? replacement = step.Step switch
        {
            CopyStep copy => new CopyStep(oldSource, copy.OverwriteMode),
            MoveStep move => new MoveStep(oldSource, move.OverwriteMode),
            _ => null
        };

        if (replacement is null) return;

        await Pipeline.ReplaceStep(step, replacement, step.CustomName);
        await ApplySourcePathCoreAsync(oldDestination);
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
            DisposeDirectoryWatcher();

            SourcePath = normalizedPath;
            SourcePathValidationMessage = string.Empty;

            FileList.Clear();

            await DirectoryTree.ChangeRootAsync(normalizedPath, BuildScanOptions(), ct);

            _lastCommittedSourcePath = normalizedPath;
            SourcePathValidationMessage = string.Empty;

            RecordRecentSource(normalizedPath);

            // Apply filter chain
            await ApplyFiltersAsync();

            StartDirectoryWatcherIfSupported();

            if (DirectoryTree.SourceProvider is { } sp)
                await Pipeline.SetSourceContext(sp);

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

        RemoveEquivalentPath(_settings.FavouritePaths, normalizedPath);
        _settings.FavouritePaths.Insert(0, normalizedPath);

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

        if (DirectoryTree.SelectedNode is { IsDirectory: true } selectedDirectory)
        {
            await FileList.LoadFilesForNodeAsync(selectedDirectory, chain, _appContext);
        }
        else
        {
            await FileList.ApplyChainToFilesAsync(chain, _appContext, ct);
        }

        await DirectoryTree.ApplyFiltersAsync(chain, _appContext, ct);

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
        Pipeline.SetSelectedBytes(totalBytes);
        StatusBar.Selection.UpdateStats(selected, totalBytes, filteredOut);
    }

    private static string GetPreparationMessage(PreviewReason reason) => reason switch
    {
        PreviewReason.DeleteConfirm  => "Checking for files to delete\u2026",
        PreviewReason.OverwriteCheck => "Checking for overwrites\u2026",
        _                            => "Preparing preview\u2026",
    };

    private async Task PreviewPipelineAsync(PreviewReason reason = PreviewReason.Manual)
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

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is not { } mainWindow)
        {
            return;
        }

        var runner = new PipelineRunner(pipeline);
        var job = new PipelineJob
        {
            RootNode         = rootNode,
            SourceProvider   = sourceProvider,
            ProviderRegistry = _providerRegistry,
            TrashService     = _trashService,
        };

        // Put the view into "preparing" state and open the dialog immediately
        // so the user sees a progress indicator rather than the app freezing.
        Preview.BeginPreparation(GetPreparationMessage(reason));
        var dialog = new PreviewView { DataContext = Preview };
        var dialogTask = dialog.ShowDialog<bool?>(mainWindow);

        // Generate the plan concurrently, tied to dialog lifetime.
        // If the user closes the dialog before the plan is ready, we cancel generation.
        using var previewCts = new CancellationTokenSource();

        // Cancel plan generation the moment the dialog closes.
        var _ = dialogTask.ContinueWith(
            _ => previewCts.Cancel(),
            TaskScheduler.Default);

        OperationPlan? plan = null;
        try
        {
            plan = await runner.PreviewAsync(job, previewCts.Token);

            // OverwriteCheck: if the plan shows no actual overwrites, skip confirmation
            // and run directly — no need to bother the user with a benign preview.
            if (reason == PreviewReason.OverwriteCheck &&
                !plan.Actions.Any(a => a.DestinationResult == DestinationResult.Overwritten))
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => dialog.Close(false));
                await dialogTask;
                await ExecutePipelineAsync(runner, job);
                return;
            }

            // Plan is ready — populate the view on the UI thread.
            Avalonia.Threading.Dispatcher.UIThread.Post(() => Preview.LoadFrom(plan));
        }
        catch (OperationCanceledException)
        {
            // User closed the dialog before plan generation finished — nothing to do.
            await dialogTask;
            return;
        }

        // Wait for user's decision (Run or Close).
        var confirmRun = await dialogTask;
        if (confirmRun == true)
        {
            await ExecutePipelineAsync(runner, job);
        }
    }

    private async Task RunPipelineAsync()
    {
        var pipeline = Pipeline.BuildLivePipeline();

        // Mandatory preview for destructive pipelines, unless the user has opted out.
        bool needsPreview = false;
        if (pipeline.HasDeleteStep && !_settings.AllowDeleteWithoutPreview) needsPreview = true;
        if (!needsPreview && !_settings.AllowOverwriteWithoutPreview)
            needsPreview = await HasPotentialOverwriteAsync(pipeline);

        if (needsPreview)
        {
            var reason = pipeline.HasDeleteStep
                ? PreviewReason.DeleteConfirm
                : PreviewReason.OverwriteCheck;
            await PreviewPipelineAsync(reason);
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
            TrashService     = _trashService,
        };

        await ExecutePipelineAsync(runner, job);
    }

    /// <summary>
    /// Returns true when the pipeline has at least one Copy or Move step with
    /// <c>OverwriteMode != Skip</c> whose destination directory already exists.
    /// If the destination doesn't exist yet, no files can be overwritten, so the
    /// mandatory-preview guard is unnecessary.
    /// </summary>
    private async Task<bool> HasPotentialOverwriteAsync(TransformPipeline pipeline)
    {
        foreach (var step in pipeline.Steps)
        {
            string? destPath = step switch
            {
                CopyStep copy when copy.OverwriteMode != OverwriteMode.Skip => copy.DestinationPath,
                MoveStep move when move.OverwriteMode != OverwriteMode.Skip => move.DestinationPath,
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(destPath))
                continue;

            var provider = _providerRegistry.ResolveProvider(destPath);
            if (provider is null)
                continue;

            if (await provider.ExistsAsync(destPath, CancellationToken.None))
                return true;
        }

        return false;
    }

    private async Task ExecutePipelineAsync(PipelineRunner runner, PipelineJob job)
    {
        var nodeProgress = new Progress<TransformResult>(OnNodeCompleted);
        var executionJob = StatusBar.Progress.Begin(job with
        {
            NodeProgress = nodeProgress,
            StepStarted = index =>
                Avalonia.Threading.Dispatcher.UIThread.Post(() => Pipeline.SetActiveStep(index)),
        });

        Pipeline.IsRunning = true;
        FilterChain.IsLocked = true;

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
            Pipeline.ClearActiveStep();
            Pipeline.IsRunning = false;
            FilterChain.IsLocked = false;
            FileList.RemoveAllMarkedForRemoval();
            DirectoryTree.RemoveNodesMarkedForRemoval();
            await ApplyPendingWatcherBatchesAsync();
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

        await ApplyWorkflowConfig(preset.Config);

        await ApplySourcePathCoreAsync(SourcePath);
    }

    /// <summary>
    /// Applies a <see cref="WorkflowConfig"/> to the current session without
    /// triggering a directory scan — just restores source, filters, and pipeline.
    /// </summary>
    private async Task ApplyWorkflowConfig(WorkflowConfig config)
    {
        DisposeDirectoryWatcher();
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

        await Pipeline.LoadPreset(pipelinePreset);
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
            ? Path.Combine(System.AppContext.BaseDirectory, "session.sc2session")
            : _appContext.DataStore.GetFilePath("session.sc2session");

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

    private void StartDirectoryWatcherIfSupported()
    {
        DisposeDirectoryWatcher();

        var provider = DirectoryTree.SourceProvider;
        var rootNode = DirectoryTree.RootNode;
        if (provider is null || rootNode is null || !provider.Capabilities.CanWatch)
        {
            return;
        }

        _directoryWatcher = _watcherFactory.Create(provider, rootNode.FullPath);
        _directoryWatcher.PendingBatchesAvailable += OnDirectoryWatcherPendingBatchesAvailable;
        _directoryWatcher.WatcherError += OnDirectoryWatcherError;
        _directoryWatcher.NotifyNodeWillBeRemoved += OnDirectoryWatcherNodeWillBeRemoved;
        _directoryWatcher.Start();
    }

    private void DisposeDirectoryWatcher()
    {
        if (_directoryWatcher is null)
        {
            return;
        }

        _directoryWatcher.PendingBatchesAvailable -= OnDirectoryWatcherPendingBatchesAvailable;
        _directoryWatcher.WatcherError -= OnDirectoryWatcherError;
        _directoryWatcher.NotifyNodeWillBeRemoved -= OnDirectoryWatcherNodeWillBeRemoved;
        _directoryWatcher.Stop();
        _directoryWatcher.Dispose();
        _directoryWatcher = null;
    }

    private void OnDirectoryWatcherPendingBatchesAvailable(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = ApplyPendingWatcherBatchesAsync());
    }

    private void OnDirectoryWatcherError(object? sender, Exception error)
    {
        Debug.WriteLine($"[Watcher] {error}");
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            LogPanel.AddEntry(
                $"Filesystem watcher warning: {error.Message}. Live updates may be incomplete; use Rescan to refresh.",
                LogLevel.Warning));
    }

    private void OnDirectoryWatcherNodeWillBeRemoved(object? sender, string[] relativeSegments)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            DirectoryTree.RootNode?.FindNodeByPathSegments(relativeSegments)?.MarkForRemoval());
    }

    private async Task ApplyPendingWatcherBatchesAsync(CancellationToken ct = default)
    {
        if (Pipeline.IsRunning)
        {
            return;
        }

        if (!await _watcherApplyGate.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            var appliedAny = false;
            var watcher = _directoryWatcher;
            if (watcher is null)
            {
                return;
            }

            while (watcher.HasPendingBatches)
            {
                foreach (var batch in watcher.DrainPendingBatches())
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }

                    Debug.Assert(!Pipeline.IsRunning);

                    if (DirectoryTree.ApplyWatcherBatch(batch))
                    {
                        appliedAny = true;                        
                    }
                }                
            }

            if (appliedAny && !Pipeline.IsRunning && !ct.IsCancellationRequested)
            {
                await ApplyFiltersAsync(ct);
            }
        }
        finally
        {
            _watcherApplyGate.Release();
        }
    }

    private static ITrashService CreateTrashService()
    {
        if (OperatingSystem.IsWindows()) return new WindowsTrashService();
        if (OperatingSystem.IsLinux())   return new FreedesktopTrashService();
        if (OperatingSystem.IsMacOS())   return new MacOsTrashService();
        return new NullTrashService();
    }

#if DEBUG
    private void SetupMemoryFileSystem()
    {
        // Create an in-memory virtual file system for testing.
        long? capacity = LimitMemoryFileSystemCapacity ? 100_000_000_000 : null;
        _memoryProvider = MockMemoryFileSystemFactory.CreateSeeded(artificialDelay: AddArtificialDelay, capacity: capacity);
        _providerRegistry.Register(_memoryProvider);

        // Enable the AddArtificialDelay and LimitMemoryFileSystemCapacity menu items
    }
#endif
}
