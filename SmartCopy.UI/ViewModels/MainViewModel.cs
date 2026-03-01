using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Progress;
using SmartCopy.Core.Selection;
using SmartCopy.Core.Settings;
using SmartCopy.Core.Workflows;
using SmartCopy.UI.Helpers;
using SmartCopy.UI.Services;
using SmartCopy.UI.ViewModels.Workflows;
using SmartCopy.UI.Views;
using SmartCopy.UI.Views.Workflows;

namespace SmartCopy.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private const int MaxRecentSources = 10;    // TODO: make this configurable

    [ObservableProperty]
    private string _sourcePath = string.Empty;

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

    private readonly MemoryFileSystemProvider _memoryProvider;
    private readonly AppSettings _settings = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly SessionStore _sessionStore = new();
    private readonly OperationJournal _operationJournal = new();
    private readonly WorkflowPresetStore _workflowStore = new();
    private readonly SelectionManager _selectionManager = new();
    private readonly SelectionSerializer _selectionSerializer = new();
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _runCts;

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
        var presetStore = new FilterPresetStore();

        _memoryProvider = MockMemoryFileSystemFactory.CreateSeeded(artificialDelay: true);
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
        };

        DirectoryTree.SetAsSourcePathRequested += async (_, path) =>
        {
            SourcePath = path;
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

    partial void OnSelectedSourceBookmarkChanged(SourceBookmarkItem? value)
    {
        // Only populate the text field — don't apply until the user commits
        // (Enter key or dropdown close after mouse selection).
        if (value is not null)
            SourcePath = value.Path;
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
        _selectionManager.SelectAll(DirectoryTree.RootNodes);
        RefreshIdleStats();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        _selectionManager.ClearAll(DirectoryTree.RootNodes);
        RefreshIdleStats();
    }

    [RelayCommand]
    private void InvertSelection()
    {
        _selectionManager.InvertAll(DirectoryTree.RootNodes);
        RefreshIdleStats();
    }

    [RelayCommand]
    private async Task SaveSelectionAsText()
    {
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
        var snapshot = _selectionManager.Capture(DirectoryTree.RootNodes, UseAbsolutePathsForSelection);
        await _selectionSerializer.SaveAsync(path, snapshot);
    }

    [RelayCommand]
    private async Task SaveSelectionAsPlaylist()
    {
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
        var snapshot = _selectionManager.Capture(DirectoryTree.RootNodes, UseAbsolutePathsForSelection);
        await _selectionSerializer.SaveAsync(path, snapshot);
    }

    [RelayCommand]
    private async Task RestoreSelection()
    {
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
        var result = _selectionManager.Restore(DirectoryTree.RootNodes, snapshot);
        RefreshIdleStats();

        if (result.HasUnmatched)
            Debug.WriteLine($"[Selection] Restored {result.MatchedCount} of {snapshot.Paths.Count}; "
                + $"{result.UnmatchedPaths.Count} unmatched: {string.Join(", ", result.UnmatchedPaths)}");
    }

    private static Window? GetMainWindow()
        => Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d
            ? d.MainWindow : null;

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
        var path = PathHelper.RemoveTrailingSeparator(SourcePath.Trim());
        if (string.IsNullOrWhiteSpace(path) || _settings.FavouritePaths.Contains(path))
            return;

        _settings.FavouritePaths.Insert(0, path);
        RefreshSourceBookmarks();
        await _settingsStore.SaveAsync(_settings);
    }

    [RelayCommand]
    private async Task RemoveSourceBookmark(SourceBookmarkItem? item)
    {
        if (item is null) return;

        bool removed;
        if (item.IsBookmark)
        {
            removed = _settings.FavouritePaths.Remove(item.Path);
        }
        else
        {
            removed = _settings.RecentSources.Remove(item.Path);
        }

        if (removed)
        {
            RefreshSourceBookmarks();
            await _settingsStore.SaveAsync(_settings);
        }
    }

    private void RecordRecentSource(string path)
    {
        path = PathHelper.RemoveTrailingSeparator(path);
        
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
        // Preserve the current text — Clear() nulls SelectedItem which
        // causes the editable ComboBox to wipe its Text binding.
        var currentPath = PathHelper.RemoveTrailingSeparator(SourcePath);

        SourceBookmarks.Clear();
        var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Clean trailing slashes out of existing entries first
        var normalizedFavourites = _settings.FavouritePaths
            .Select(PathHelper.RemoveTrailingSeparator)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        
        var normalizedRecent = _settings.RecentSources
            .Select(PathHelper.RemoveTrailingSeparator)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

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
        {
            CollectStatsRecursive(root, ref selected, ref totalBytes, ref filteredOut);
        }

        Pipeline.SetSelectedIncludedFileCount(selected);
        StatusBar.Selection.UpdateStats(selected, totalBytes, filteredOut);
    }

    private static void CollectStatsRecursive(DirectoryTreeNode node, ref int selected, ref long totalBytes, ref int filteredOut)
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

        if (saved.RestoreLastSourcePath && !saved.RestoreLastWorkflow
            && saved.LastSourcePath is { Length: > 0 })
        {
            SourcePath = saved.LastSourcePath;
        }
        RefreshSourceBookmarks();

        // Phase 1: hardcode /mem/Mirror as the mirror-filter comparison path.
        FilterChain.PipelineDestinationPath = MockMemoryFileSystemFactory.TargetPath;
        await _operationJournal.RotateAsync(_settings.LogRetentionDays);
        await WorkflowMenu.RefreshAsync();

        // Restore last workflow if the option is enabled and a session snapshot exists.
        if (saved.RestoreLastWorkflow)
        {
            try
            {
                var session = await _sessionStore.LoadAsync(GetSessionPath());
                if (session is not null)
                    await ApplyWorkflowConfigAsync(session);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                Debug.WriteLine($"Failed to restore session snapshot: {ex}");
            }
        }

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

        LogPanel.AddEntry("SmartCopy 2026 ready — memory source loaded");
    }

    private async Task PreviewPipelineAsync()
    {
        var pipeline = Pipeline.BuildLivePipeline();
        if (DirectoryTree.RootNodes.Count == 0)
            return;

        var rootNode = DirectoryTree.RootNodes.First();
        Pipeline.SetSelectedIncludedFileCount(rootNode.GetSelectedDescendants().Count(n => !n.IsDirectory));

        if (!Pipeline.CanRun)
            return;

        var runner = new PipelineRunner(pipeline);
        var job = new PipelineJob
        {
            RootNode       = rootNode,
            SourceProvider = _memoryProvider,
            TargetProvider = _memoryProvider,
            OverwriteMode  = ParseOverwriteMode(_settings.DefaultOverwriteMode),
            DeleteMode     = ParseDeleteMode(_settings.DefaultDeleteMode),
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

        if (DirectoryTree.RootNodes.Count == 0)
            return;

        var rootNode = DirectoryTree.RootNodes.First();
        Pipeline.SetSelectedIncludedFileCount(rootNode.GetSelectedDescendants().Count(n => !n.IsDirectory));

        if (!Pipeline.CanRun)
            return;

        var runner = new PipelineRunner(pipeline);
        var job = new PipelineJob
        {
            RootNode       = rootNode,
            SourceProvider = _memoryProvider,
            TargetProvider = _memoryProvider,
            OverwriteMode  = ParseOverwriteMode(_settings.DefaultOverwriteMode),
            DeleteMode     = ParseDeleteMode(_settings.DefaultDeleteMode),
        };
        await ExecutePipelineAsync(runner, job);
    }

    private async Task ExecutePipelineAsync(PipelineRunner runner, PipelineJob job)
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();

        StatusBar.Progress.Begin(_runCts);
        var progress = new Progress<OperationProgress>(StatusBar.Progress.Update);
        var nodeProgress = new Progress<TransformResult>(OnNodeCompleted);

        if (AutoOpenLogOnRun)
            LogPanel.IsExpanded = true;

        try
        {
            var results = await runner.ExecuteAsync(job, progress, nodeProgress, _runCts.Token);

            await _operationJournal.WriteAsync(results.Where(r => r.SourcePathResult != SourcePathResult.None));

            foreach (var r in results)
            {
                if (!r.IsSuccess)
                {
                    LogPanel.AddEntry($"Failed: {Path.GetFileName(r.SourcePath)}", LogLevel.Error);
                }
                else if (r.SourcePathResult == SourcePathResult.Copied)
                {
                    LogPanel.AddEntry($"Copied {Path.GetFileName(r.SourcePath)} → {r.DestinationPath} ({FileSizeFormatter.FormatBytes(r.OutputBytes)})");
                }
                else if (r.SourcePathResult == SourcePathResult.Moved)
                {
                    LogPanel.AddEntry($"Moved {Path.GetFileName(r.SourcePath)} → {r.DestinationPath}");
                }
                else if (r.SourcePathResult is SourcePathResult.Trashed or SourcePathResult.Deleted)
                {
                    LogPanel.AddEntry($"Deleted {Path.GetFileName(r.SourcePath)}");
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
            DirectoryTree.RemoveAllMarkedForRemoval();
            FileList.RemoveAllMarkedForRemoval();
        }
    }

    private void OnNodeCompleted(TransformResult result)
    {
        if (!result.IsSuccess)
            return;

        if (result.SourcePathResult is SourcePathResult.Moved or SourcePathResult.Trashed or SourcePathResult.Deleted)
        {
            DirectoryTreeNode? node = DirectoryTree.MarkForRemoval(result.SourcePath);
            if (node is not null)
            {
                if (node.IsDirectory)
                {
                    FileList.ClearIfUnder(node);
                }
            }
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

        await ApplyWorkflowConfigAsync(preset.Config);
    }

    /// <summary>
    /// Applies a <see cref="WorkflowConfig"/> to the current session without saving
    /// or recording any names — just restores source, filters, and pipeline.
    /// </summary>
    private async Task ApplyWorkflowConfigAsync(WorkflowConfig config)
    {
        // Restore source path
        SourcePath = config.SourcePath;
        await ApplySourcePathCoreAsync(config.SourcePath);

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
            : SessionStore.GetDefaultSessionPath();

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
