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

    public static string FormatRate(double bytesPerSecond)
    {
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond <= 0)
            return "0B/s";

        return bytesPerSecond switch
        {
            < 1024 => $"{bytesPerSecond.ToString("F0", CultureInfo.InvariantCulture)}B/s",
            < 1024 * 1024 => $"{(bytesPerSecond / 1024).ToString("F1", CultureInfo.InvariantCulture)}KB/s",
            < 1024L * 1024 * 1024 => $"{(bytesPerSecond / (1024.0 * 1024)).ToString("F1", CultureInfo.InvariantCulture)}MB/s",
            _ => $"{(bytesPerSecond / (1024.0 * 1024 * 1024)).ToString("F2", CultureInfo.InvariantCulture)}GB/s",
        };
    }
}
