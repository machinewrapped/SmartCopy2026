using MediaDevices;
using System.Runtime.Versioning;

namespace SmartCopy.Core.FileSystem;

[SupportedOSPlatform("windows")]
public sealed class MtpFileSystemProvider : IFileSystemProvider, IDisposable
{
    private readonly MediaDevice _device;
    private static readonly char[] Separators = ['/', '\\'];

    /// <param name="rootPath">
    /// The scan root for this provider instance — typically the folder the user selected
    /// (e.g. "mtp://Samsung/DCIM/Camera/") rather than always the device root.
    /// This allows two providers on the same device with different roots to coexist in the registry.
    /// </param>
    public MtpFileSystemProvider(MediaDevice device, string rootPath)
    {
        _device = MtpConnectionManager.Acquire(device, this);
        var name = string.IsNullOrEmpty(_device.FriendlyName) ? _device.Model : _device.FriendlyName;
        VolumeId = $"mtp://{name}";
        RootPath = rootPath;
    }

    public MediaDevice Device => _device;
    public string RootPath { get; }
    /// <summary>Identifies the MTP device (acts as the volume identifier).</summary>
    public string? VolumeId { get; }
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
        => Task.Run(() =>
        {
            var devicePath = DevicePath(path);
            // segments: ["mtp:", DeviceName, dir1, dir2, ..., filename]
            // parent dirs are segments[2..^1]; skip if file is directly under device root
            var segments = SplitPath(path);
            if (segments.Length > 2)
                EnsureDeviceDirectoryExists(DevicePath(JoinPath(VolumeId + "/", segments[2..^1])));
            _device.UploadFile(data, devicePath);
        }, ct);

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
        => Task.Run(() => EnsureDeviceDirectoryExists(DevicePath(path)), ct);

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

    public void Dispose() => MtpConnectionManager.Release(this);

    /// <summary>
    /// Ensures every segment of <paramref name="devicePath"/> exists on the device,
    /// creating missing directories one level at a time.
    /// </summary>
    private void EnsureDeviceDirectoryExists(string devicePath)
    {
        var segments = devicePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = "";
        foreach (var segment in segments)
        {
            current += "/" + segment;
            if (!_device.DirectoryExists(current))
                _device.CreateDirectory(current);
        }
    }

    /// <summary>
    /// Strips the "mtp://DeviceName" prefix to get the device-native absolute path.
    /// Uses <c>_devicePrefix</c> (not <c>RootPath</c>) so this works correctly even when
    /// <c>RootPath</c> is a subfolder like "mtp://Samsung/DCIM/Camera/".
    /// </summary>
    private string DevicePath(string mtpPath)
    {
        if (mtpPath.StartsWith(VolumeId!, StringComparison.OrdinalIgnoreCase))
        {
            var rest = mtpPath[VolumeId!.Length..];
            return rest.Length > 0 ? rest : "/";
        }
        return mtpPath;
    }

    private static string JoinMtp(string parent, string child)
        => parent.TrimEnd('/') + "/" + child;
}
