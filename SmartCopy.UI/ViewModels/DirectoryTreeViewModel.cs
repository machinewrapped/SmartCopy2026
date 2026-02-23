using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;

namespace SmartCopy.UI.ViewModels;

public class DirectoryTreeViewModel(IFileSystemProvider provider, string rootPath) : ViewModelBase
{
    private readonly IFileSystemProvider _provider = provider;
    private string _rootPath = rootPath;
    private FileSystemNode? _selectedNode;

    public ObservableCollection<FileSystemNode> RootNodes { get; } = [];

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
        RootNodes.Clear();

        var root = await BuildNodeTreeAsync(_rootPath, ct);
        root.IsExpanded = true;
        RootNodes.Add(root);
        SelectedNode = root;

        if (!string.IsNullOrWhiteSpace(initialSelectionPath))
        {
            SelectByPath(initialSelectionPath);
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
            RelativePath = ComputeRelativePath(_rootPath, sourceNode.FullPath),
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

    private static string ComputeRelativePath(string rootPath, string fullPath)
    {
        if (fullPath.Equals(rootPath, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        var root = rootPath.EndsWith('/') || rootPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootPath
            : rootPath + '/';

        if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return fullPath[root.Length..];

        return Path.GetFileName(fullPath);
    }
}
