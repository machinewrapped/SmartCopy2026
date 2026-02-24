using CommunityToolkit.Mvvm.ComponentModel;

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
                ? $"No files selected  ·  {filteredOut} filtered out"
                : "No files selected";
            return;
        }

        var sizeText = FormatBytes(totalBytes);
        StatusText = filteredOut > 0
            ? $"{fileCount} files selected  ·  {sizeText}  ·  {filteredOut} filtered out"
            : $"{fileCount} files selected  ·  {sizeText}";
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB",
    };
}
