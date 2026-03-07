namespace SmartCopy.Core.Trash;

public sealed class FreedesktopTrashService : ITrashService
{
    public bool IsAvailable => OperatingSystem.IsLinux();

    public async Task TrashAsync(string fullPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
            throw new FileNotFoundException($"Path does not exist: {fullPath}", fullPath);

        var trashDir = GetTrashDirectory();
        var filesDir = Path.Combine(trashDir, "files");
        var infoDir = Path.Combine(trashDir, "info");

        Directory.CreateDirectory(filesDir);
        Directory.CreateDirectory(infoDir);

        var baseName = Path.GetFileName(fullPath);
        var destName = ResolveUniqueName(filesDir, baseName);
        var destPath = Path.Combine(filesDir, destName);
        var infoPath = Path.Combine(infoDir, destName + ".trashinfo");

        var trashInfo =
            $"[Trash Info]\nPath={fullPath}\nDeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n";

        await File.WriteAllTextAsync(infoPath, trashInfo, ct);

        if (Directory.Exists(fullPath))
            Directory.Move(fullPath, destPath);
        else
            File.Move(fullPath, destPath);
    }

    private static string GetTrashDirectory()
    {
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdgDataHome))
            return Path.Combine(xdgDataHome, "Trash");

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "Trash");
    }

    private static string ResolveUniqueName(string filesDir, string baseName)
    {
        if (!File.Exists(Path.Combine(filesDir, baseName)) &&
            !Directory.Exists(Path.Combine(filesDir, baseName)))
            return baseName;

        var nameWithoutExt = Path.GetFileNameWithoutExtension(baseName);
        var ext = Path.GetExtension(baseName);

        for (int i = 2; i < int.MaxValue; i++)
        {
            var candidate = $"{nameWithoutExt}_{i}{ext}";
            if (!File.Exists(Path.Combine(filesDir, candidate)) &&
                !Directory.Exists(Path.Combine(filesDir, candidate)))
                return candidate;
        }

        return $"{baseName}_{Guid.NewGuid():N}";
    }
}
