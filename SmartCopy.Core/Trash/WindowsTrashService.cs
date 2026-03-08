namespace SmartCopy.Core.Trash;

public sealed class WindowsTrashService : ITrashService
{
    public bool IsAvailable => OperatingSystem.IsWindows();

    public Task TrashAsync(string fullPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return Task.Run(() =>
        {
            if (File.Exists(fullPath))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    fullPath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            else if (Directory.Exists(fullPath))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                    fullPath,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            else
            {
                throw new FileNotFoundException($"Path does not exist: {fullPath}", fullPath);
            }
        }, ct);
    }
}
