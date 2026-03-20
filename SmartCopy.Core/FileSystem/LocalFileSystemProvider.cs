namespace SmartCopy.Core.FileSystem;

public sealed class LocalFileSystemProvider : IFileSystemProvider
{
    private const int CopyBufferSize = 256 * 1024;
    private const long SmallFileProgressThresholdBytes = 10L * 1024 * 1024;

    private readonly bool _isNetworkPath;
    private readonly ProviderCapabilities _capabilities;

    public LocalFileSystemProvider(string rootPath, Func<string>? readLinuxMountInfo = null)
    {
        RootPath = NormalizePath(rootPath);
        _isNetworkPath = LocalPathNetworkClassifier.IsNetworkPath(RootPath, readLinuxMountInfo);
        _capabilities = new ProviderCapabilities(
            CanSeek: true,
            CanAtomicMove: !_isNetworkPath,
            CanWatch: !_isNetworkPath,
            MaxPathLength: int.MaxValue,
            CanTrash: !_isNetworkPath,
            CanQueryFreeSpace: !_isNetworkPath);
    }

    public string RootPath { get; }

    public string? VolumeId => _isNetworkPath
        ? null
        : Path.GetPathRoot(RootPath)?.ToUpperInvariant();

    public ProviderCapabilities Capabilities => _capabilities;

    public Task<IReadOnlyList<FileSystemNode>> GetChildrenAsync(string path, CancellationToken ct)
    {
        return Task.Run<IReadOnlyList<FileSystemNode>>(() =>
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Resolve(path);

            if (!Directory.Exists(fullPath))
            {
                throw new DirectoryNotFoundException(fullPath);
            }

            var nodes = new List<FileSystemNode>();

            foreach (var childDirectory in Directory.EnumerateDirectories(fullPath))
            {
                ct.ThrowIfCancellationRequested();
                nodes.Add(CreateDirectoryNode(childDirectory));
            }

            foreach (var childFile in Directory.EnumerateFiles(fullPath))
            {
                ct.ThrowIfCancellationRequested();
                nodes.Add(CreateFileNode(childFile));
            }

            return nodes;
        }, ct);
    }

    public Task<FileSystemNode> GetNodeAsync(string path, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Resolve(path);

            if (Directory.Exists(fullPath))
            {
                return CreateDirectoryNode(fullPath);
            }

            if (File.Exists(fullPath))
            {
                return CreateFileNode(fullPath);
            }

            throw new FileNotFoundException($"Path does not exist: {fullPath}", fullPath);
        }, ct);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct)
    {
        return Task.Run<Stream>(() =>
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Resolve(path);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"File does not exist: {fullPath}", fullPath);
            }

            return new FileStream(
                fullPath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    BufferSize = CopyBufferSize,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });
        }, ct);
    }

    public async Task WriteAsync(string path, Stream data, IProgress<long>? progress, CancellationToken ct)
    {
        var fullPath = Resolve(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var output = new FileStream(
            fullPath,
            new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = CopyBufferSize,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });

        if (TryGetRemainingLength(data, out var remainingBytes) &&
            remainingBytes <= SmallFileProgressThresholdBytes)
        {
            await data.CopyToAsync(output, CopyBufferSize, ct);
            if (progress is not null && remainingBytes > 0)
            {
                progress.Report(remainingBytes);
            }

            return;
        }

        var buffer = new byte[CopyBufferSize];

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var read = await data.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read == 0)
            {
                break;
            }

            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            progress?.Report(read);
        }
    }

    private static bool TryGetRemainingLength(Stream data, out long remainingBytes)
    {
        if (!data.CanSeek)
        {
            remainingBytes = 0;
            return false;
        }

        remainingBytes = Math.Max(0, data.Length - data.Position);
        return true;
    }

    public Task DeleteAsync(string path, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Resolve(path);

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return;
            }

            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
                return;
            }

            throw new FileNotFoundException($"Path does not exist: {fullPath}", fullPath);
        }, ct);
    }

    public Task MoveAsync(string sourcePath, string destPath, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var fullSourcePath = Resolve(sourcePath);
            var fullDestinationPath = Resolve(destPath);

            var destinationParent = Path.GetDirectoryName(fullDestinationPath);
            if (!string.IsNullOrEmpty(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }

            if (File.Exists(fullSourcePath))
            {
                if (File.Exists(fullDestinationPath))
                {
                    File.Delete(fullDestinationPath);
                }

                File.Move(fullSourcePath, fullDestinationPath);
                return;
            }

            if (Directory.Exists(fullSourcePath))
            {
                if (Directory.Exists(fullDestinationPath))
                {
                    Directory.Delete(fullDestinationPath, recursive: true);
                }

                Directory.Move(fullSourcePath, fullDestinationPath);
                return;
            }

            throw new FileNotFoundException($"Source path does not exist: {fullSourcePath}", fullSourcePath);
        }, ct);
    }

    public Task CreateDirectoryAsync(string path, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Resolve(path);
            Directory.CreateDirectory(fullPath);
        }, ct);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Resolve(path);
            return File.Exists(fullPath) || Directory.Exists(fullPath);
        }, ct);
    }

    public Task<long?> GetAvailableFreeSpaceAsync(CancellationToken ct)
    {
        if (_isNetworkPath) return Task.FromResult<long?>(null);
        try
        {
            var drive = new DriveInfo(RootPath);
            return Task.FromResult<long?>(drive.AvailableFreeSpace);
        }
        catch
        {
            return Task.FromResult<long?>(null);
        }
    }

    public string GetRelativePath(string basePath, string fullPath)
    {
        string normalBase = NormalizePath(basePath);
        string normalFull = NormalizePath(fullPath);
        var root = PathHelper.EnsureTrailingSeparator(normalBase);
        var relative = Path.GetRelativePath(root, normalFull);
        return relative == "." ? string.Empty : relative;
    }

    public string[] SplitPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return [];
        // Split on OS-native separators only.
        // On Linux '\' is a valid filename character, not a separator.
        var sep = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
        return path.Trim(sep).Split(sep, StringSplitOptions.RemoveEmptyEntries);
    }

    public string JoinPath(string basePath, IReadOnlyList<string> segments)
    {
        if (segments.Count == 0)
            return NormalizePath(basePath);
        return NormalizePath(Path.Combine(basePath, Path.Combine([.. segments])));
    }

    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Path.DirectorySeparatorChar.ToString();
        }

        var full = Path.GetFullPath(path);
        return PathHelper.RemoveTrailingSeparator(full);
    }

    private string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return RootPath;
        }

        if (Path.IsPathFullyQualified(path))
        {
            return NormalizePath(path);
        }

        return CombinePath(RootPath, path);
    }

    private static string CombinePath(string basePath, string relativePath)
    {
        if (Path.IsPathFullyQualified(relativePath))
        {
            return NormalizePath(relativePath);
        }

        if (string.IsNullOrWhiteSpace(basePath))
        {
            return NormalizePath(relativePath);
        }

        return NormalizePath(Path.Combine(basePath, relativePath));
    }

    private static FileSystemNode CreateDirectoryNode(string directoryPath)
    {
        var info = new DirectoryInfo(directoryPath);
        return new FileSystemNode
        {
            Name = info.Name,
            FullPath = info.FullName,
            IsDirectory = true,
            Size = 0,
            CreatedAt = info.CreationTimeUtc,
            ModifiedAt = info.LastWriteTimeUtc,
            Attributes = info.Attributes,
        };
    }

    private static FileSystemNode CreateFileNode(string filePath)
    {
        var info = new FileInfo(filePath);
        return new FileSystemNode
        {
            Name = info.Name,
            FullPath = info.FullName,
            IsDirectory = false,
            Size = info.Length,
            CreatedAt = info.CreationTimeUtc,
            ModifiedAt = info.LastWriteTimeUtc,
            Attributes = info.Attributes,
        };
    }

    private string GetCanonicalPath(string path)
    {
        var segments = SplitPath(path);
        return string.Join("/", segments);
    }
}
