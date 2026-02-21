using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.UI.ViewModels;

public class FileListViewModel : ViewModelBase
{
    private readonly IFileSystemProvider _provider;
    private string _directoryPath;
    private CancellationTokenSource? _loadCts;

    private IReadOnlyList<FileSystemNode> _files = [];
    public IReadOnlyList<FileSystemNode> Files
    {
        get => _files;
        private set => SetProperty(ref _files, value);
    }

    public FileListViewModel(IFileSystemProvider provider, string directoryPath)
    {
        _provider = provider;
        _directoryPath = directoryPath;
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
                if (child.IsDirectory)
                {
                    continue;
                }

                var checkState = directoryNode.CheckState == CheckState.Checked ? CheckState.Checked : CheckState.Unchecked;

                var filterResult = child.Name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
                    ? FilterResult.Excluded
                    : FilterResult.Included;

                files.Add(new FileSystemNode
                {
                    Name = child.Name,
                    FullPath = child.FullPath,
                    RelativePath = child.RelativePath,
                    IsDirectory = false,
                    Size = child.Size,
                    CreatedAt = child.CreatedAt,
                    ModifiedAt = child.ModifiedAt,
                    Attributes = child.Attributes,
                    CheckState = checkState,
                    FilterResult = filterResult,
                    ExcludedByFilter = filterResult == FilterResult.Excluded ? "Hidden" : null,
                    Parent = directoryNode
                });
            }

            if (ct.IsCancellationRequested)
            {
                return;
            }

            foreach (var file in files)
            {
                directoryNode.Files.Add(file);
            }
        }

        Files = directoryNode.Files.ToList();
    }
}
