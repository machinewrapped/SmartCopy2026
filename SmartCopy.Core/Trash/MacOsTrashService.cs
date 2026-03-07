namespace SmartCopy.Core.Trash;

public sealed class MacOsTrashService : ITrashService
{
    public bool IsAvailable => OperatingSystem.IsMacOS();

    public async Task TrashAsync(string fullPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            throw new FileNotFoundException($"Path does not exist: {fullPath}", fullPath);

        var escaped = fullPath.Replace("'", "\\'");
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "osascript",
            Arguments = $"-e 'tell application \"Finder\" to delete POSIX file \"{escaped}\"'",
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start osascript process.");

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(ct);
            throw new IOException($"osascript failed (exit {process.ExitCode}): {error}");
        }
    }
}
