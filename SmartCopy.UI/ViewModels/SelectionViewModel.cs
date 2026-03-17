using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.FileSystem;
namespace SmartCopy.UI.ViewModels;

public partial class SelectionViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _statusText = "No files selected";

    public void UpdateStats(int selectedFiles, long selectedBytes, int totalIncluded, long totalIncludedBytes, int filteredOut)
    {
        if (selectedFiles == 0)
        {
            StatusText = filteredOut > 0
                ? $"No files selected  ·  {filteredOut} {(filteredOut == 1 ? "file" : "files")} filtered out"
                : "No files selected";
            return;
        }

        var filesText = selectedFiles == totalIncluded
            ? $"{selectedFiles} {(selectedFiles == 1 ? "file" : "files")} selected"
            : $"{selectedFiles} of {totalIncluded} files selected";

        var bytesText = selectedFiles == totalIncluded
            ? FileSizeFormatter.FormatBytes(selectedBytes)
            : $"{FileSizeFormatter.FormatBytes(selectedBytes)} of {FileSizeFormatter.FormatBytes(totalIncludedBytes)}";

        StatusText = filteredOut > 0
            ? $"{filesText}  ·  {bytesText}  ·  {filteredOut} {(filteredOut == 1 ? "file" : "files")} filtered out"
            : $"{filesText}  ·  {bytesText}";
    }

}
