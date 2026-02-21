using System.Collections.ObjectModel;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.UI.ViewModels;

public class FileListViewModel : ViewModelBase
{
    private readonly IFileSystemProvider _provider;
    private string _directoryPath;

    public ObservableCollection<FileSystemNode> Files { get; } = new();

    public FileListViewModel(IFileSystemProvider provider, string directoryPath)
    {
        _provider = provider;
        _directoryPath = directoryPath;
        LoadFilesForDirectory(_directoryPath);
    }

    public void LoadFilesForDirectory(string directoryPath)
    {
        _directoryPath = directoryPath;
        Files.Clear();

        var children = _provider.GetChildrenAsync(_directoryPath, CancellationToken.None).GetAwaiter().GetResult();
        foreach (var child in children)
        {
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

            Files.Add(new FileSystemNode
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
    }
}
