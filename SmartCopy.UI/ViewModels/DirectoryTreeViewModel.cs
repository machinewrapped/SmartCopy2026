using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;

namespace SmartCopy.UI.ViewModels;

public class DirectoryTreeViewModel : ViewModelBase
{
    private readonly IFileSystemProvider _provider;
    private string _rootPath;
    private FileSystemNode? _selectedNode;
    private bool _isLoading;

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>Raised when any node's <see cref="FileSystemNode.CheckState"/> changes.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>Raised when the user requests a node's path be set as the source root.</summary>
    public event EventHandler<string>? SetAsSourcePathRequested;

    public void RequestSetAsSourcePath(string path) =>
        SetAsSourcePathRequested?.Invoke(this, path);

    public ObservableCollection<FileSystemNode> RootNodes { get; } = [];

    public DirectoryTreeViewModel(IFileSystemProvider provider, string rootPath)
    {
        _provider = provider;
        _rootPath = rootPath;
    }

    public FileSystemNode? SelectedNode
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
    /// <see cref="FileSystemNode.FilterResult"/> and <see cref="FileSystemNode.ExcludedByFilter"/>.
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

        try
        {
            var root = await BuildNodeTreeAsync(_rootPath, ct);
            root.IsExpanded = true;
            root.PropertyChanged += OnRootNodePropertyChanged;
            RootNodes.Add(root);
            SelectedNode = root;

            if (!string.IsNullOrWhiteSpace(initialSelectionPath))
            {
                SelectByPath(initialSelectionPath);
            }
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

    private void OnRootNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // null property name = batch reset; CheckState or IsSelected = individual change
        if (e.PropertyName is null or nameof(FileSystemNode.CheckState) or nameof(FileSystemNode.IsSelected))
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<FileSystemNode> BuildNodeTreeAsync(string path, CancellationToken ct)
    {
        var sourceRoot = await _provider.GetNodeAsync(path, ct);
        var root = CloneNode(sourceRoot, parent: null);

        var stack = new Stack<FileSystemNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();
            if (!current.IsDirectory)
            {
                continue;
            }

            var children = await _provider.GetChildrenAsync(current.FullPath, ct);
            foreach (var child in children)
            {
                var clonedChild = CloneNode(child, current);
                if (child.IsDirectory)
                {
                    current.Children.Add(clonedChild);
                    stack.Push(clonedChild);
                }
                else
                {
                    current.Files.Add(clonedChild);
                }
            }
        }

        return root;
    }

    private FileSystemNode CloneNode(FileSystemNode sourceNode, FileSystemNode? parent)
    {
        return new FileSystemNode
        {
            Name = sourceNode.Name,
            FullPath = sourceNode.FullPath,
            RelativePathSegments = _provider.SplitPath(_provider.GetRelativePath(_rootPath, sourceNode.FullPath)),
            IsDirectory = sourceNode.IsDirectory,
            Size = sourceNode.Size,
            CreatedAt = sourceNode.CreatedAt,
            ModifiedAt = sourceNode.ModifiedAt,
            Attributes = sourceNode.Attributes,
            Parent = parent,
            CheckState = sourceNode.CheckState,
            FilterResult = sourceNode.FilterResult,
            ExcludedByFilter = sourceNode.ExcludedByFilter,
            Notes = sourceNode.Notes,
            IsExpanded = sourceNode.IsExpanded,
        };
    }

    private FileSystemNode? FindByPath(string fullPath)
    {
        var stack = new Stack<FileSystemNode>(RootNodes);
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

}
