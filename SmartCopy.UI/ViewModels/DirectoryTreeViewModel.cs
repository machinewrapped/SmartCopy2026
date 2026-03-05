using System.Collections.ObjectModel;
using System.ComponentModel;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Scanning;

namespace SmartCopy.UI.ViewModels;

public class DirectoryTreeViewModel : ViewModelBase
{
    private readonly FileSystemProviderRegistry _providerRegistry;
    private DirectoryTreeNode? _selectedNode;
    private bool _isLoading;

    /// <summary>The root nodes of the directory tree.</summary>
    public DirectoryTreeNode? RootNode => ItemsSource.Any() ? ItemsSource.First() : null;

    /// <summary>Indicates whether the directory tree is currently loading.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>Indicates whether the directory tree is fully loaded</summary>
    public bool IsLoaded { get; private set; }

    /// <summary>The filesystem that contains the root node</summary>
    public IFileSystemProvider? SourceProvider { get; private set; }

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
    private ObservableCollection<DirectoryTreeNode> ItemsSource { get; } = [];

    /// <summary>
    /// Set the root path for the directory tree (may be underneath the filesystem root)
    /// </summary>
    public async Task ChangeRootAsync(string newRootPath, CancellationToken ct = default)
    {
        await InitializeAsync(newRootPath, ct: ct);
    }

    /// <summary>
    /// Applies <paramref name="chain"/> to every node in the tree, setting <see cref="DirectoryTreeNode.FilterResult"/>/>.
    /// </summary>
    public async Task ApplyFiltersAsync(
        FilterChain chain,
        IFilterContext? context = null,
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
        ItemsSource.Clear();
        SourceProvider = null;
        IsLoaded = false;
        IsLoading = false;
    }

    private async Task InitializeAsync(string rootPath, CancellationToken ct = default)
    {
        // Unsubscribe from old roots before clearing
        if (RootNode != null)
        {
            RootNode.PropertyChanged -= OnRootNodePropertyChanged;
            Reset();
        }

        try
        {
            IsLoading = true;

            var scanOptions = new ScanOptions { LazyExpand = false, IncludeHidden = true };

            var sourceProvider = _providerRegistry.Resolve(rootPath)
                ?? throw new ArgumentException("Source path cannot be mapped to a FileSystemProvider");

            var scanner = new DirectoryScanner(sourceProvider);
            
            await foreach (var node in scanner.ScanAsync(rootPath, scanOptions, ct: ct))
            {
                // First node yielded is our root node
                if (!ItemsSource.Any())
                {
                    ItemsSource.Add(node);

                    SourceProvider = sourceProvider;

                    node.IsExpanded = true;
                    node.PropertyChanged += OnRootNodePropertyChanged;
                }

                // subsequent nodes are wired into the tree by the scanner
            }
        }
        finally
        {
            IsLoading = false;
            IsLoaded = RootNode != null;
        }

        if (RootNode is not null)
        {
            // Calculate stats and clear dirty flags
            RootNode.BuildStats();

            // Default to root node, if user hasn't selected one during the scan
            SelectedNode ??= RootNode;
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
}
