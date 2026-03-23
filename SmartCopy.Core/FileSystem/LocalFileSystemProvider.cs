namespace SmartCopy.Core.FileSystem;

/// <summary>
/// <see cref="IFileSystemProvider"/> backed by <see cref="System.IO"/> and the local (or UNC) filesystem.
/// Network paths are detected at construction time; they lose atomic-move, trash, watch, and free-space
/// capabilities because the underlying OS APIs either don't apply or are unreliable over the network.
/// </summary>
public sealed class LocalFileSystemProvider : IFileSystemProvider
{
    private readonly bool _isNetworkPath;
    private readonly ProviderCapabilities _capabilities;
    private readonly LocalFileSystemProviderOptions _options;

    public LocalFileSystemProvider(
        string rootPath,
        Func<string>? readLinuxMountInfo = null,
        LocalFileSystemProviderOptions? options = null)
    {
        RootPath = NormalizePath(rootPath);
        _options = (options ?? LocalFileSystemProviderOptions.Default).Normalize();
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
                    BufferSize = _options.CopyBufferSizeBytes,
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
                BufferSize = _options.CopyBufferSizeBytes,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });

        long? remainingBytes = TryGetRemainingLength(data, out var knownRemainingBytes)
            ? knownRemainingBytes
            : null;

        if (remainingBytes is long bytesRemaining &&
            _options.PreallocateDestinationFile &&
            bytesRemaining > 0)
        {
            output.SetLength(bytesRemaining);
        }

        var writeMode = DetermineWriteMode(progress, remainingBytes);

        if (writeMode == LocalFileSystemWriteMode.CopyToAsync)
        {
            // CopyToAsync mode: wraps the source in a ProgressReportingReadStream so the
            // framework's internal buffer loop reports progress without a manual loop here.
            await CopyViaCopyToAsync(data, output, progress, ct);
            return;
        }

        if (writeMode == LocalFileSystemWriteMode.Auto &&
            remainingBytes is long autoBytesRemaining &&
            autoBytesRemaining <= _options.SmallFileProgressThresholdBytes)
        {
            // Small-file optimisation: for files whose full size fits in memory we know
            // exactly how many bytes will be written, so we let the framework copy without
            // overhead and report progress in one shot at the end instead of per-chunk.
            await data.CopyToAsync(output, _options.CopyBufferSizeBytes, ct);
            if (progress is not null && autoBytesRemaining > 0)
            {
                progress.Report(autoBytesRemaining);
            }

            return;
        }

        // Large files and unknown-length streams: manual loop reports progress per chunk,
        // which keeps UI responsive during long transfers without adding a stream wrapper.
        await CopyWithManualLoopAsync(data, output, progress, ct);
    }

    // Write strategy — private helpers for WriteAsync above.

    private LocalFileSystemWriteMode DetermineWriteMode(IProgress<long>? progress, long? remainingBytes)
    {
        if (_options.WriteMode != LocalFileSystemWriteMode.Auto)
            return _options.WriteMode;
        // No progress handler: CopyToAsync with no wrapper is the fastest path.
        if (progress is null)
            return LocalFileSystemWriteMode.CopyToAsync;
        // Unknown length: can't apply the small-file optimisation, go straight to manual loop.
        if (remainingBytes is null)
            return LocalFileSystemWriteMode.ManualLoop;
        // Known length with progress: WriteAsync resolves the heuristic inline.
        return LocalFileSystemWriteMode.Auto;
    }

    private async Task CopyViaCopyToAsync(
        Stream data,
        Stream output,
        IProgress<long>? progress,
        CancellationToken ct)
    {
        Stream source = data;
        if (progress is not null)
        {
            source = new ProgressReportingReadStream(data, progress);
        }

        await source.CopyToAsync(output, _options.CopyBufferSizeBytes, ct);
    }

    private async Task CopyWithManualLoopAsync(
        Stream data,
        Stream output,
        IProgress<long>? progress,
        CancellationToken ct)
    {
        if (_options.UseArrayPoolForManualLoop)
        {
            var rentedBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(_options.CopyBufferSizeBytes);
            try
            {
                await CopyWithManualLoopCoreAsync(data, output, rentedBuffer, progress, ct);
            }
            finally
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(rentedBuffer);
            }

            return;
        }

        var buffer = new byte[_options.CopyBufferSizeBytes];
        await CopyWithManualLoopCoreAsync(data, output, buffer, progress, ct);
    }

    private static async Task CopyWithManualLoopCoreAsync(
        Stream data,
        Stream output,
        byte[] buffer,
        IProgress<long>? progress,
        CancellationToken ct)
    {
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

    // Path utilities — all paths passed to public methods may be absolute or root-relative;
    // Resolve() converts them to absolute before any System.IO call.

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


}
