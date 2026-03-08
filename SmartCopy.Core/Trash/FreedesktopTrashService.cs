namespace SmartCopy.Core.Trash;

public sealed class FreedesktopTrashService : ITrashService
{
    public bool IsAvailable => OperatingSystem.IsLinux();

    public async Task TrashAsync(string fullPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            throw new FileNotFoundException($"Path does not exist: {fullPath}", fullPath);

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "gio",
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("trash");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(fullPath);

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start gio process.");

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(ct);
            throw new IOException($"gio trash failed (exit {process.ExitCode}): {error}");
        }
    }
}
