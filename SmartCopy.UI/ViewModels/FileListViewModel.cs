using System.Collections.Specialized;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;

namespace SmartCopy.UI.ViewModels;

public class FileListViewModel : ViewModelBase
{
    private CancellationTokenSource? _loadCts;

    private DirectoryTreeNode? _currentDirectoryNode;

    // The subset (or whole set) exposed to the DataGrid, respecting ShowFilteredFiles.
    private IReadOnlyList<DirectoryTreeNode> _visibleFiles = [];
    public IReadOnlyList<DirectoryTreeNode> VisibleFiles
    {
        get => _visibleFiles;
        private set => SetProperty(ref _visibleFiles, value);
    }

    private bool _showFilteredFiles = true;
    /// <summary>
    /// When <c>true</c>, excluded files are still visible (dimmed).
    /// When <c>false</c>, excluded files are hidden from the list.
    /// </summary>
    public bool ShowFilteredFiles
    {
        get => _showFilteredFiles;
        set
        {
            if (SetProperty(ref _showFilteredFiles, value))
                RefreshVisibleFiles();
        }
    }

    /// <summary>
    /// Applies the filter chain to the current file list.
    /// </summary>
    public async Task ApplyChainToFilesAsync(FilterChain filterChain, IPathResolver pathResolver, CancellationToken ct = default)
    {
        if (_currentDirectoryNode != null)
        {
            if (_currentDirectoryNode.IsMarkedForRemoval)
            {
                Clear();
                return;
            }

            await filterChain.ApplyToTreeAsync(_currentDirectoryNode, pathResolver, ct);
        }

        RefreshVisibleFiles();
    }

    /// <summary>
    /// Displays the files already scanned into <paramref name="directoryNode"/>,
    /// then applies the current filter chain.
    /// </summary>
    public async Task LoadFilesForNodeAsync(DirectoryTreeNode directoryNode, FilterChain filterChain, IPathResolver pathResolver)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        SetCurrentDirectoryNode(directoryNode);

        await ApplyChainToFilesAsync(filterChain, pathResolver, ct);
    }

    public DirectoryTreeNode? FindFile(string fullPath)
    {
        return _currentDirectoryNode?.Files.FirstOrDefault(f =>
            string.Equals(f.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    public void RemoveFile(string fullPath)
    {
        var node = FindFile(fullPath);
        if (node is null) return;

        _currentDirectoryNode?.Files.Remove(node);
    }

    public void RemoveAllMarkedForRemoval()
    {
        if (_currentDirectoryNode is null) return;

        if (_currentDirectoryNode.IsMarkedForRemoval)
        {
            Clear();
            return;
        }

        RefreshVisibleFiles();
    }

    public void Clear()
    {
        SetCurrentDirectoryNode(null);
        RefreshVisibleFiles();
    }

    public void ClearIfUnder(DirectoryTreeNode removedDirectory)
    {
        var node = _currentDirectoryNode;
        while (node is not null)
        {
            if (node == removedDirectory)
            {
                Clear();
                return;
            }
            node = node.Parent;
        }
    }

    private void RefreshVisibleFiles()
    {
        var files = _currentDirectoryNode?.Files ?? [];
        VisibleFiles = _showFilteredFiles
            ? [.. files]
            : [.. files.Where(f => f.FilterResult == FilterResult.Included)];
    }

    private void SetCurrentDirectoryNode(DirectoryTreeNode? directoryNode)
    {
        if (ReferenceEquals(_currentDirectoryNode, directoryNode))
        {
            return;
        }

        if (_currentDirectoryNode is not null)
        {
            _currentDirectoryNode.Files.CollectionChanged -= OnCurrentDirectoryFilesChanged;
        }

        _currentDirectoryNode = directoryNode;

        if (_currentDirectoryNode is not null)
        {
            _currentDirectoryNode.Files.CollectionChanged += OnCurrentDirectoryFilesChanged;
        }
    }

    private void OnCurrentDirectoryFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshVisibleFiles();
    }
}
