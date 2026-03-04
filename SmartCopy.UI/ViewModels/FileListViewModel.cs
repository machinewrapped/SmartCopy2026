using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Filters;

namespace SmartCopy.UI.ViewModels;

public class FileListViewModel : ViewModelBase
{
    public FileListViewModel(IFilterContext filterContext)
    {
        _filterContext = filterContext;
    }

    private CancellationTokenSource? _loadCts;

    private readonly IFilterContext _filterContext;
    private DirectoryTreeNode? _currentDirectoryNode;

    // The full unfiltered set of file nodes for the current directory.
    private List<DirectoryTreeNode> _files = [];

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
    public async Task ApplyChainToFilesAsync(FilterChain filterChain, CancellationToken ct = default)
    {
        if (_currentDirectoryNode != null)
        {
            await filterChain.ApplyToTreeAsync(_currentDirectoryNode, _filterContext, ct);
        }

        RefreshVisibleFiles();
    }

    /// <summary>
    /// Displays the files already scanned into <paramref name="directoryNode"/>,
    /// then applies the current filter chain.
    /// </summary>
    public async Task LoadFilesForNodeAsync(DirectoryTreeNode directoryNode, FilterChain filterChain)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        _currentDirectoryNode = directoryNode;
        _files = [.. directoryNode.Files];

        await ApplyChainToFilesAsync(filterChain, ct);
    }

    public DirectoryTreeNode? FindFile(string fullPath)
    {
        return _files.FirstOrDefault(f =>
            string.Equals(f.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    public void RemoveFile(string fullPath)
    {
        var node = FindFile(fullPath);
        if (node is null) return;

        _files.Remove(node);
        _currentDirectoryNode?.Files.Remove(node);
        RefreshVisibleFiles();
    }

    public void RemoveAllMarkedForRemoval()
    {
        if (_currentDirectoryNode is null) return;

        if (_currentDirectoryNode.IsMarkedForRemoval)
        {
            _files.Clear();
            _currentDirectoryNode = null;
            VisibleFiles = [];
            return;
        }

        _files.RemoveAll(f => f.IsMarkedForRemoval);
        RefreshVisibleFiles();
    }

    public void Clear()
    {
        _files.Clear();
        _currentDirectoryNode = null;
        RefreshVisibleFiles();
    }

    public void ClearIfUnder(DirectoryTreeNode removedDirectory)
    {
        var node = _currentDirectoryNode;
        while (node is not null)
        {
            if (node == removedDirectory)
            {
                _files.Clear();
                _currentDirectoryNode = null;
                RefreshVisibleFiles();
                return;
            }
            node = node.Parent;
        }
    }

    private void RefreshVisibleFiles()
    {
        VisibleFiles = _showFilteredFiles
            ? [.. _files]
            : [.. _files.Where(f => f.FilterResult == FilterResult.Included)];
    }
}
