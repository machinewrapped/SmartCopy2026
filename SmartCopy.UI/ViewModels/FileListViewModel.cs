using System;
using System.Collections.ObjectModel;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.UI.ViewModels;

public class FileListViewModel : ViewModelBase
{
    public ObservableCollection<FileSystemNode> Files { get; } = new();

    public FileListViewModel()
    {
        // Stub data
        Files.Add(new FileSystemNode { Name = "Come Together.flac", Size = 48_000_000, ModifiedAt = new DateTime(2024, 3, 1), CheckState = CheckState.Checked, FilterResult = FilterResult.Included });
        Files.Add(new FileSystemNode { Name = "Something.flac", Size = 32_000_000, ModifiedAt = new DateTime(2024, 3, 1), CheckState = CheckState.Unchecked, FilterResult = FilterResult.Included });
        Files.Add(new FileSystemNode { Name = "cover.jpg", Size = 420_000, ModifiedAt = new DateTime(2024, 3, 1), CheckState = CheckState.Checked, FilterResult = FilterResult.Included });
        Files.Add(new FileSystemNode { Name = "desktop.ini", Size = 1_000, ModifiedAt = new DateTime(2024, 3, 1), CheckState = CheckState.Checked, FilterResult = FilterResult.Excluded, ExcludedByFilter = "Hidden" });
    }
}
