using SmartCopy.Core.Settings;

namespace SmartCopy.Core.Logging;

public static class CrashLogger
{
    public static void Write(Exception? ex, string source)
    {
        try
        {
            var crashDir = LocalAppDataStore.ForCurrentUser().GetDirectoryPath("Crash Reports");
            Directory.CreateDirectory(crashDir);

            DateTime now = DateTime.Now;
            var timestamp = now.ToString("yyyyMMdd-HHmmss");
            var filePath = Path.Combine(crashDir, $"crash-{timestamp}.log");

            var content = $"""
                SmartCopy2026 Crash Report
                Timestamp : {now:yyyy-MM-dd HH:mm:ss}
                Source    : {source}
                Exception : {ex?.GetType().FullName ?? "(null)"}
                Message   : {ex?.Message ?? "(none)"}

                {ex?.ToString() ?? "(no stack trace)"}
                """;

            File.WriteAllText(filePath, content);
        }
        catch
        {
            // Writing the crash log itself failed — nothing more we can do.
        }
    }
}
