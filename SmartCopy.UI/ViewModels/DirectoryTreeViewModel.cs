using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Scanning;

namespace SmartCopy.UI.ViewModels;

public class DirectoryTreeViewModel : ViewModelBase
{
    private readonly DirectoryScanner _scanner;
    private string _rootPath;
    private DirectoryTreeNode? _selectedNode;
    private bool _isLoading;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>Raised when any node's <see cref="DirectoryTreeNode.CheckState"/> changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when the user requests a node's path be set as the source root.</summary>
    public event EventHandler<string>? SetAsSourcePathRequested;

    public void RequestSetAsSourcePath(string path) =>
        SetAsSourcePathRequested?.Invoke(this, path);

    public ObservableCollection<DirectoryTreeNode> RootNodes { get; } = [];

    public DirectoryTreeViewModel(IFileSystemProvider provider, string rootPath)
    {
        _rootPath = rootPath;
        _scanner = new DirectoryScanner(provider);
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

    /// <summary>
    /// Applies <paramref name="chain"/> to every node in the tree, setting
    /// <see cref="DirectoryTreeNode.FilterResult"/> and <see cref="DirectoryTreeNode.ExcludedByFilter"/>.
    /// </summary>
    public async Task ApplyFiltersAsync(
        FilterChain chain,
        IFileSystemProvider? comparisonProvider,
        CancellationToken ct = default)
    {
        await chain.ApplyToTreeAsync(RootNodes, comparisonProvider, ct);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        // Unsubscribe from old roots before clearing
        foreach (var oldRoot in RootNodes)
            oldRoot.PropertyChanged -= OnRootNodePropertyChanged;

        RootNodes.Clear();
        IsLoading = true;

        DirectoryTreeNode? root = null;
        try
        {
            var scanOptions = new ScanOptions { LazyExpand = false, IncludeHidden = true };
            await foreach (var node in _scanner.ScanAsync(_rootPath, scanOptions, ct: ct))
            {
                if (root is null)
                {
                    root = node;
                    root.IsExpanded = true;
                    root.PropertyChanged += OnRootNodePropertyChanged;
                    RootNodes.Add(root);
                }
                // subsequent nodes are already wired into the tree by the scanner
            }

            if (root is not null)
                SelectedNode = root;
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task ChangeRootAsync(string newRootPath, CancellationToken ct = default)
    {
        _rootPath = newRootPath;
        await InitializeAsync(ct: ct);
    }

    public void RemoveNodesMarkedForRemoval()
    {
        foreach (var root in RootNodes)
        {
            if (root.IsMarkedForRemoval)
            {
                RootNodes.Remove(root);
            }
            else
            {
                root.RemoveNodesMarkedForRemoval();
            }
        }
    }

    private void OnRootNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // null property name = batch reset; CheckState or IsSelected = individual change
        if (e.PropertyName is null or nameof(DirectoryTreeNode.CheckState) or nameof(DirectoryTreeNode.IsSelected))
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private DirectoryTreeNode? FindNode(string fullPath)
    {
        var stack = new Stack<DirectoryTreeNode>(RootNodes);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }

            foreach (var file in node.Files)
            {
                if (string.Equals(file.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
        }

        return null;
    }
}
