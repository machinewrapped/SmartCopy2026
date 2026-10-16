using System.Collections.ObjectModel;
using System.ComponentModel;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Scanning;
using SmartCopy.Core.FileSystem.Hardware;

namespace SmartCopy.UI.ViewModels;

public class DirectoryTreeViewModel : ViewModelBase
{
    private readonly FileSystemProviderRegistry _providerRegistry;
    private DirectoryTreeNode? _selectedNode;
    private bool _isLoading;

    /// <summary>The root nodes of the directory tree.</summary>
    public DirectoryNode? RootNode => ItemsSource.Count > 0 ? ItemsSource[0] : null;

    /// <summary>Indicates whether the directory tree is currently loading.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>Indicates whether the directory tree is fully loaded</summary>
    public bool IsLoaded { get; private set; }

    private IFileSystemProvider? _sourceProvider;
    private CancellationTokenSource? _classificationCts;
    
    /// <summary>The filesystem that contains the root node</summary>
    /// <remarks>
    /// Adopted as soon as the scan yields a root so the rest of the UI can use it, but its
    /// classification is deliberately not loaded here — see <see cref="LoadClassificationAsync"/>.
    /// </remarks>
    public IFileSystemProvider? SourceProvider
    {
        get => _sourceProvider;
        private set
        {
            if (SetProperty(ref _sourceProvider, value) && value is null)
            {
                _ = LoadClassificationAsync(null);
            }
        }
    }

    private DriveClassification? _classification;
    public DriveClassification? Classification
    {
        get => _classification;
        private set => SetProperty(ref _classification, value);
    }

    /// <summary>
    /// Loads the drive classification for <paramref name="provider"/>, or clears it when null.
    /// <para>
    /// Called once the scan is idle rather than when <see cref="SourceProvider"/> is assigned. On a
    /// drive whose media type the OS will not report — anything behind a USB bridge on macOS — the
    /// classifier falls back to timing random reads, and reads issued while the recursive scan is
    /// still working the same drive measure queueing rather than seek time. A handful of contended
    /// samples is enough to report a solid-state drive as rotational, and the verdict is then cached
    /// for the session.
    /// </para>
    /// </summary>
    private async Task LoadClassificationAsync(IFileSystemProvider? provider)
    {
        _classificationCts?.Cancel();
        _classificationCts?.Dispose();
        _classificationCts = new CancellationTokenSource();
        var ct = _classificationCts.Token;

        if (provider == null)
        {
            Classification = null;
            return;
        }

        Classification = null; // Clear while loading
        try
        {
            Classification = await provider.GetClassificationAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation
        }
        catch
        {
            Classification = DriveClassification.Unknown;
        }
    }

    /// <summary>The root path of the directory tree</summary>
    public string? SourcePath => RootNode?.FullPath;

    /// <summary>Raised when any node's <see cref="DirectoryTreeNode.CheckState"/> changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when the user requests a node's path be set as the source root.</summary>
    public event EventHandler<string>? SetAsSourcePathRequested;

    /// <summary>Requests that the node at the given path be set as the working source root.</summary>
    public void RequestSetAsSourcePath(string path) =>
        SetAsSourcePathRequested?.Invoke(this, path);

    public DirectoryTreeViewModel(FileSystemProviderRegistry providerRegistry)
    {
        _providerRegistry = providerRegistry;
    }

    /// <summary>
    /// The active node, whose files the file list shows. With several tree nodes selected,
    /// this is the one most recently added to the selection.
    /// </summary>
    public DirectoryTreeNode? SelectedNode
    {
        get => _selectedNode;
        set => SetProperty(ref _selectedNode, value);
    }

    private bool _showFilteredNodesInTree = true;
    public bool ShowFilteredNodesInTree
    {
        get => _showFilteredNodesInTree;
        set => SetProperty(ref _showFilteredNodesInTree, value);
    }

    /// <summary> Avalonia TreeView requires an enumerable root</summary>
    public ObservableCollection<DirectoryNode> ItemsSource { get; } = [];

    /// <summary>
    /// Set the root path for the directory tree (may be underneath the filesystem root)
    /// </summary>
    public async Task ChangeRootAsync(string newRootPath, ScanOptions scanOptions, CancellationToken ct = default)
    {
        await InitializeAsync(newRootPath, scanOptions, ct);
    }

    /// <summary>
    /// Scans the current root again, keeping the checked nodes, expanded folders and active node
    /// that still exist.
    /// </summary>
    public async Task RescanAsync(ScanOptions scanOptions, CancellationToken ct = default)
    {
        if (RootNode is not { } root)
            return;

        var provider = SourceProvider;
        var selected = SelectedNode;
        var state = DirectoryTreeState.Capture(root, selected, provider?.PathComparer ?? StringComparer.Ordinal);
        SelectedNode = null;

        var generation = _initializeCount + 1;
        try
        {
            await InitializeAsync(root.FullPath, scanOptions, ct);
        }
        catch when (_initializeCount == generation)
        {
            // Failed or cancelled, and no newer scan has replaced it: put the old tree back
            // rather than leave a partial one without the user's checks.
            RestoreRoot(root, provider, selected);
            throw;
        }

        if (RootNode is { } newRoot)
        {
            SelectedNode = state.Restore(newRoot) ?? newRoot;
            newRoot.BuildStats();
        }
    }

    /// <summary>
    /// Applies <paramref name="chain"/> to every node in the tree, setting <see cref="DirectoryTreeNode.FilterResult"/>/>.
    /// </summary>
    public async Task ApplyFiltersAsync(
        FilterChain chain,
        IPathResolver? context = null,
        CancellationToken ct = default)
    {
        if (RootNode == null) 
            return;

        await chain.ApplyToTreeAsync(RootNode, context, ct);
    }

    public void RemoveNodesMarkedForRemoval()
    {
        if (RootNode == null || RootNode.IsMarkedForRemoval)
        {
            Reset();
        }
        else
        {
            RootNode.RemoveNodesMarkedForRemoval();
        }
    }

    internal void Reset()
    {
        // Clear the selection too: the view only does so when the TreeView raises SelectionChanged
        // on clear, and a node left over from the old tree would survive into the new one.
        SelectedNode = null;
        ItemsSource.Clear();
        SourceProvider = null;
        IsLoaded = false;
        IsLoading = false;
    }

    private void RestoreRoot(DirectoryNode root, IFileSystemProvider? provider, DirectoryTreeNode? selected)
    {
        if (RootNode is { } partial)
            partial.PropertyChanged -= OnRootNodePropertyChanged;

        ItemsSource.Clear();
        ItemsSource.Add(root);
        root.PropertyChanged += OnRootNodePropertyChanged;

        SourceProvider = provider;
        IsLoaded = true;
        SelectedNode = selected ?? root;
        _ = LoadClassificationAsync(provider);
    }

    /// <summary>Counts scans started, so a scan can tell whether a newer one has replaced it.</summary>
    private int _initializeCount;

    private async Task InitializeAsync(string rootPath, ScanOptions scanOptions, CancellationToken ct = default)
    {
        var generation = ++_initializeCount;

        // Unsubscribe from old roots before clearing
        if (RootNode != null)
        {
            RootNode.PropertyChanged -= OnRootNodePropertyChanged;
            Reset();
        }

        try
        {
            IsLoading = true;

            var sourceProvider = _providerRegistry.ResolveProvider(rootPath)
                ?? throw new ArgumentException("Source path cannot be mapped to a FileSystemProvider");

            var scanner = new DirectoryScanner(sourceProvider);
            
            await foreach (var node in scanner.ScanAsync(rootPath, scanOptions, ct: ct))
            {
                ct.ThrowIfCancellationRequested();
                
                // First node yielded is our root node
                if (ItemsSource.Count == 0 && node is DirectoryNode dirNode)
                {
                    ItemsSource.Add(dirNode);

                    SourceProvider = sourceProvider;

                    dirNode.IsExpanded = true;
                    dirNode.PropertyChanged += OnRootNodePropertyChanged;
                }

                // subsequent nodes are wired into the tree by the scanner
            }
        }
        finally
        {
            // A scan replaced by a newer one ends after that one has started; the loading state
            // is the newer scan's, and clearing it here would enable Rescan and Run mid-scan.
            if (generation == _initializeCount)
            {
                IsLoading = false;
                IsLoaded = RootNode != null;
            }
        }

        if (RootNode is not null)
        {
            // Calculate stats and clear dirty flags
            RootNode.BuildStats();

            // Default to root node, if user hasn't selected one during the scan
            SelectedNode ??= RootNode;

            // Now the drive is idle, so a latency-based media-type probe measures the device.
            _ = LoadClassificationAsync(SourceProvider);
        }
    }

    private CancellationTokenSource? _selectionCts;

    private void OnRootNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DirectoryTreeNode.IsDirty)
            && sender is DirectoryTreeNode { IsDirty: true })
        {
            ScheduleSelectionChangedAsync();
        }
    }

    private async void ScheduleSelectionChangedAsync()
    {
        _selectionCts?.Cancel();
        _selectionCts?.Dispose();
        _selectionCts = new CancellationTokenSource();
        var ct = _selectionCts.Token;
        try
        {
            await Task.Delay(100, ct);

            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
    }

    internal bool ApplyWatcherBatch(DirectoryWatcherBatch batch)
    {
        if (RootNode is null)
        {
            return false;
        }

        if (batch.RequiresFullRescan)
        {
            // Error handling and full-rescan fallback are implemented in a later milestone.
            return false;
        }

        var result = DirectoryTreePatcher.Apply(RootNode, batch, SelectedNode);
        SelectedNode = result.SelectedNode ?? SelectedNode;
        RemoveNodesMarkedForRemoval();
        return true;
    }
}
