using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.FileSystem;
namespace SmartCopy.UI.ViewModels;

public partial class SelectionViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "No files selected";

    public void UpdateStats(int fileCount, long totalBytes, int filteredOut)
    {
        if (fileCount == 0)
        {
            StatusText = filteredOut > 0
                ? $"No files selected  ·  {filteredOut} files filtered out"
                : "No files selected";
            return;
        }

        var sizeText = FileSizeFormatter.FormatBytes(totalBytes);
        StatusText = filteredOut > 0
            ? $"{fileCount} files selected  ·  {sizeText}  ·  {filteredOut} files filtered out"
            : $"{fileCount} files selected  ·  {sizeText}";
    }

}
