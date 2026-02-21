using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.UI.ViewModels;

public class FileListViewModel : ViewModelBase
{
    private readonly IFileSystemProvider _provider;
    private string _directoryPath;
    private CancellationTokenSource? _loadCts;

    public ObservableCollection<FileSystemNode> Files { get; } = new();

    public FileListViewModel(IFileSystemProvider provider, string directoryPath)
    {
        _provider = provider;
        _directoryPath = directoryPath;
    }

    public async Task LoadFilesForDirectoryAsync(string directoryPath)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        _directoryPath = directoryPath;
        var children = await _provider.GetChildrenAsync(_directoryPath, ct);
        var files = new List<FileSystemNode>();
        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            if (child.IsDirectory)
            {
                continue;
            }

            var checkState = child.Name switch
            {
                "Something.flac" => CheckState.Unchecked,
                _ => CheckState.Checked,
            };

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
            });
        }

        if (ct.IsCancellationRequested)
        {
            return;
        }

        Files.Clear();
        foreach (var file in files)
        {
            Files.Add(file);
        }
    }
}
