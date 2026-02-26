using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;

namespace SmartCopy.UI.ViewModels;

public class FileListViewModel(IFileSystemProvider provider, string directoryPath) : ViewModelBase
{
    private readonly IFileSystemProvider _provider = provider;
    private string _directoryPath = directoryPath;
    private CancellationTokenSource? _loadCts;

    private FilterChain? _chain;
    private IFileSystemProvider? _comparisonProvider;

    // The full unfiltered set of file nodes for the current directory.
    private List<FileSystemNode> _files = [];

    // The subset (or whole set) exposed to the DataGrid, respecting ShowFilteredFiles.
    private IReadOnlyList<FileSystemNode> _visibleFiles = [];
    public IReadOnlyList<FileSystemNode> VisibleFiles
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

    public async Task LoadFilesForNodeAsync(FileSystemNode directoryNode)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        _directoryPath = directoryNode.FullPath;

        if (directoryNode.Files.Count == 0)
        {
            var children = await _provider.GetChildrenAsync(_directoryPath, ct);
            var files = new List<FileSystemNode>();

            foreach (var child in children)
            {
                ct.ThrowIfCancellationRequested();
                if (child.IsDirectory) continue;

                var checkState = directoryNode.CheckState == CheckState.Checked
                    ? CheckState.Checked
                    : CheckState.Unchecked;

                files.Add(new FileSystemNode
                {
                    Name = child.Name,
                    FullPath = child.FullPath,
                    RelativePathSegments = child.RelativePathSegments,
                    IsDirectory = false,
                    Size = child.Size,
                    CreatedAt = child.CreatedAt,
                    ModifiedAt = child.ModifiedAt,
                    Attributes = child.Attributes,
                    CheckState = checkState,
                    FilterResult = FilterResult.Included,
                    Parent = directoryNode,
                });
            }

            if (ct.IsCancellationRequested) return;

            foreach (var file in files)
                directoryNode.Files.Add(file);
        }

        _files = [.. directoryNode.Files];

        await ApplyChainToFilesAsync(ct);
        RefreshVisibleFiles();
    }

    private async Task ApplyChainToFilesAsync(CancellationToken ct = default)
    {
        if (_chain is not null && _files.Count > 0)
            await _chain.ApplyToTreeAsync(_files, _comparisonProvider, ct);
    }

    private void RefreshVisibleFiles()
    {
        VisibleFiles = _showFilteredFiles
            ? _files
            : [.. _files.Where(f => f.FilterResult == FilterResult.Included)];
    }
}
