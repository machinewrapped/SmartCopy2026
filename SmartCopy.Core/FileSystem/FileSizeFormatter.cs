using System.Globalization;

namespace SmartCopy.Core.FileSystem;

public static class FileSizeFormatter
{
    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes}B",
        < 1024 * 1024 => $"{(bytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture)}KB",
        < 1024L * 1024 * 1024 => $"{(bytes / (1024.0 * 1024)).ToString("F1", CultureInfo.InvariantCulture)}MB",
        _ => $"{(bytes / (1024.0 * 1024 * 1024)).ToString("F2", CultureInfo.InvariantCulture)}GB",
    };
}
