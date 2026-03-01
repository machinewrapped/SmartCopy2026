using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;

namespace SmartCopy.UI.ViewModels;

public class FileListViewModel : ViewModelBase
{
    private CancellationTokenSource? _loadCts;

    private FilterChain? _chain;
    private IFileSystemProvider? _comparisonProvider;
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

    /// <summary>Stores the active filter chain for use in subsequent load and reapply calls.</summary>
    public void UpdateChain(FilterChain? chain, IFileSystemProvider? comparisonProvider)
    {
        _chain = chain;
        _comparisonProvider = comparisonProvider;
    }

    /// <summary>
    /// Re-applies the current filter chain to the already-loaded file nodes
    /// and refreshes <see cref="VisibleFiles"/>.
    /// </summary>
    public async Task ReapplyFiltersAsync(CancellationToken ct = default)
    {
        await ApplyChainToFilesAsync(ct);
        RefreshVisibleFiles();
    }

    /// <summary>
    /// Displays the files already scanned into <paramref name="directoryNode"/>,
    /// then applies the current filter chain.
    /// </summary>
    public async Task LoadFilesForNodeAsync(DirectoryTreeNode directoryNode)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        _currentDirectoryNode = directoryNode;
        _files = [.. directoryNode.Files];

        await ApplyChainToFilesAsync(ct);
        RefreshVisibleFiles();
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

    private async Task ApplyChainToFilesAsync(CancellationToken ct = default)
    {
        if (_chain is not null && _files.Count > 0)
            await _chain.ApplyToTreeAsync(_files, _comparisonProvider, ct);
    }

    private void RefreshVisibleFiles()
    {
        VisibleFiles = _showFilteredFiles
            ? [.. _files]
            : [.. _files.Where(f => f.FilterResult == FilterResult.Included)];
    }
}
