using System.Collections.ObjectModel;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.UI.ViewModels;

public class DirectoryTreeViewModel : ViewModelBase
{
    private readonly IFileSystemProvider _provider;
    private readonly string _rootPath;
    private FileSystemNode? _selectedNode;

    public ObservableCollection<FileSystemNode> RootNodes { get; } = new();
    public FileSystemNode? SelectedNode
    {
        get => _selectedNode;
        set => SetProperty(ref _selectedNode, value);
    }

    public DirectoryTreeViewModel(IFileSystemProvider provider, string rootPath)
    {
        _provider = provider;
        _rootPath = rootPath;
        LoadTree();
    }

    private void LoadTree()
    {
        RootNodes.Clear();

        var root = BuildNodeRecursive(_rootPath, parent: null);
        root.CheckState = CheckState.Indeterminate;
        RootNodes.Add(root);
        SelectedNode = root;
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

    private FileSystemNode BuildNodeRecursive(string path, FileSystemNode? parent)
    {
        var sourceNode = _provider.GetNodeAsync(path, CancellationToken.None).GetAwaiter().GetResult();
        var node = new FileSystemNode
        {
            Name = sourceNode.Name,
            FullPath = sourceNode.FullPath,
            RelativePath = sourceNode.RelativePath,
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
        };

        if (!node.IsDirectory)
        {
            return node;
        }

        var children = _provider.GetChildrenAsync(path, CancellationToken.None).GetAwaiter().GetResult();
        foreach (var child in children)
        {
            if (!child.IsDirectory)
            {
                continue;
            }

            node.Children.Add(BuildNodeRecursive(child.FullPath, node));
        }

        return node;
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
