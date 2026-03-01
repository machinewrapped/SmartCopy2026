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

    public async Task InitializeAsync(string? initialSelectionPath = null, CancellationToken ct = default)
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

            if (!string.IsNullOrWhiteSpace(initialSelectionPath))
                SelectByPath(initialSelectionPath);
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

    public bool SelectByPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return false;
        }

        var match = FindByPath(fullPath);
        if (match is null)
        {
            return false;
        }

        SelectedNode = match;
        return true;
    }

    /// <summary>
    /// Removes the directory node at <paramref name="fullPath"/> from the tree.
    /// Returns <c>true</c> if a node was found and removed; <c>false</c> if not found
    /// (e.g. the path belongs to a file, which lives in FileListViewModel).
    /// </summary>
    public bool RemoveNode(string fullPath)
    {
        var node = FindByPath(fullPath);
        if (node is null) return false;

        if (node.Parent is not null)
        {
            var parent = node.Parent;
            parent.Children.Remove(node);
            FilterChain.RecalculateParentExclusion(parent);
        }
        else
        {
            RootNodes.Remove(node);
        }

        return true;
    }

    private void OnRootNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // null property name = batch reset; CheckState or IsSelected = individual change
        if (e.PropertyName is null or nameof(DirectoryTreeNode.CheckState) or nameof(DirectoryTreeNode.IsSelected))
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private DirectoryTreeNode? FindByPath(string fullPath)
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
        }

        return null;
    }

    public IReadOnlyList<DirectoryTreeNode> CollectSelectedFiles()
    {
        var selected = new List<DirectoryTreeNode>();
        foreach (var root in RootNodes)
        {
            CollectSelectedNodesRecursive(root, selected);
        }
        return selected;
    }

    public IReadOnlyList<DirectoryTreeNode> CollectAllIncludedFiles()
    {
        var all = new List<DirectoryTreeNode>();
        foreach (var root in RootNodes)
        {
            CollectAllIncludedFilesRecursive(root, all);
        }
        return all;
    }

    private static void CollectSelectedNodesRecursive(DirectoryTreeNode node, List<DirectoryTreeNode> output)
    {
        if (node.IsDirectory && node.IsSelected)
        {
            output.Add(node); // atomic — all descendants selected and filter-included
            return;           // do NOT recurse into children
        }

        output.AddRange(node.Files.Where(f => f.IsSelected)); // individual file selection#

        foreach (var child in node.Children)
        {
            CollectSelectedNodesRecursive(child, output);
        }
    }

    private static void CollectAllIncludedFilesRecursive(DirectoryTreeNode node, List<DirectoryTreeNode> output)
    {
        output.AddRange(node.Files.Where(f => f.FilterResult == FilterResult.Included));

        foreach (var child in node.Children)
        {
            CollectAllIncludedFilesRecursive(child, output);
        }
    }
}
