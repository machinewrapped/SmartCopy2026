using Avalonia.Platform.Storage;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.UI.Views;

internal static class PathPickerBrowseLocation
{
    public static async Task<IStorageFolder?> ResolveAsync(
        IStorageProvider storageProvider, string? path, bool isFilePath = false)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            var normalized = PathHelper.NormalizeUserPath(path);
            if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            {
                if (!uri.IsFile) return null;
                normalized = uri.LocalPath;
            }

            var folder = await storageProvider.TryGetFolderFromPathAsync(normalized);
            if (folder is not null || !isFilePath) return folder;

            var parent = Path.GetDirectoryName(normalized);
            return string.IsNullOrEmpty(parent)
                ? null
                : await storageProvider.TryGetFolderFromPathAsync(parent);
        }
        catch
        {
            // An unavailable or invalid current path should not prevent browsing.
            return null;
        }
    }
}
