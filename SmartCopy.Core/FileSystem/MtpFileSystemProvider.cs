#if WINDOWS
using MediaDevices;

namespace SmartCopy.Core.FileSystem;

public sealed class MtpFileSystemProvider : IFileSystemProvider, IDisposable
{
    private readonly MediaDevice _device;
    private static readonly char[] Separators = ['/', '\\'];

    public MtpFileSystemProvider(MediaDevice device)
    {
        _device = device;
        _device.Connect();
        var name = string.IsNullOrEmpty(device.FriendlyName) ? device.Model : device.FriendlyName;
        RootPath = $"mtp://{name}/";
    }

    public string RootPath { get; }
    public string? VolumeId => null;
    public ProviderCapabilities Capabilities => new(
        CanSeek: false, CanAtomicMove: false, CanWatch: false,
        MaxPathLength: 260, CanTrash: false);

    public Task<IReadOnlyList<FileSystemNode>> GetChildrenAsync(string path, CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<FileSystemNode>>(() =>
        {
            var dirInfo = _device.GetDirectoryInfo(DevicePath(path));
            var result = new List<FileSystemNode>();

            foreach (var dir in dirInfo.EnumerateDirectories())
            {
                result.Add(new FileSystemNode
                {
                    Name = dir.Name,
                    FullPath = JoinMtp(path, dir.Name),
                    IsDirectory = true,
                });
            }

            foreach (var file in dirInfo.EnumerateFiles())
            {
                result.Add(new FileSystemNode
                {
                    Name = file.Name,
                    FullPath = JoinMtp(path, file.Name),
                    IsDirectory = false,
                    Size = (long)file.Length,
                    ModifiedAt = file.LastWriteTime ?? DateTime.MinValue,
                });
            }

            return result;
        }, ct);
    }

    public Task<FileSystemNode> GetNodeAsync(string path, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var devicePath = DevicePath(path);
            if (_device.DirectoryExists(devicePath))
            {
                var info = _device.GetDirectoryInfo(devicePath);
                return new FileSystemNode { Name = info.Name, FullPath = path, IsDirectory = true };
            }
            else
            {
                var info = _device.GetFileInfo(devicePath);
                return new FileSystemNode
                {
                    Name = info.Name,
                    FullPath = path,
                    IsDirectory = false,
                    Size = (long)info.Length,
                    ModifiedAt = info.LastWriteTime ?? DateTime.MinValue,
                };
            }
        }, ct);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct)
        => Task.Run<Stream>(() => _device.GetFileInfo(DevicePath(path)).OpenRead(), ct);

    public Task WriteAsync(string path, Stream data, IProgress<long>? progress, CancellationToken ct)
        => Task.Run(() => _device.UploadFile(data, DevicePath(path)), ct);

    public Task DeleteAsync(string path, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var devicePath = DevicePath(path);
            if (_device.DirectoryExists(devicePath))
                _device.DeleteDirectory(devicePath);
            else
                _device.DeleteFile(devicePath);
        }, ct);
    }

    public Task MoveAsync(string sourcePath, string destPath, CancellationToken ct)
        => throw new NotSupportedException("MTP does not support atomic moves.");

    public Task CreateDirectoryAsync(string path, CancellationToken ct)
        => Task.Run(() => _device.CreateDirectory(DevicePath(path)), ct);

    public Task<long?> GetAvailableFreeSpaceAsync(CancellationToken ct)
        => Task.FromResult<long?>(null);

    public Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            var devicePath = DevicePath(path);
            return _device.FileExists(devicePath) || _device.DirectoryExists(devicePath);
        }, ct);
    }

    public string GetRelativePath(string basePath, string fullPath)
    {
        if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            return fullPath[basePath.Length..].TrimStart('/');
        return fullPath;
    }

    public string[] SplitPath(string path)
        => path.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

    public string JoinPath(string basePath, IReadOnlyList<string> segments)
    {
        if (segments.Count == 0) return basePath;
        return basePath.TrimEnd('/') + "/" + string.Join("/", segments);
    }

    public void Dispose() => _device.Disconnect();

    /// <summary>Strips "mtp://DeviceName/" prefix to get the device-native path.</summary>
    private string DevicePath(string mtpPath)
    {
        if (mtpPath.StartsWith(RootPath, StringComparison.OrdinalIgnoreCase))
            return "/" + mtpPath[RootPath.Length..];
        return mtpPath;
    }

    private static string JoinMtp(string parent, string child)
        => parent.TrimEnd('/') + "/" + child;
}
#endif
