namespace SmartCopy.Core.FileSystem;

/// <summary>
/// <see cref="IFileSystemProvider"/> backed by <see cref="System.IO"/> and the local (or UNC) filesystem.
/// Network paths are detected at construction time; they lose atomic-move, trash, watch, and free-space
/// capabilities because the underlying OS APIs either don't apply or are unreliable over the network.
/// </summary>
public sealed class LocalFileSystemProvider : IFileSystemProvider
{
    private const int MaxFileNameLength = 255;
    private const string StagedFilePrefix = ".";
    private const string StagedFileSuffix = ".smartcopy.tmp.";
    private const string CompactStagedFilePrefix = ".smartcopy.staging.";
    private const int GuidNLength = 32;
    private const int DefaultBufferSize = 256 * 1024;

    private readonly bool _isNetworkPath;
    private readonly ProviderCapabilities _capabilities;
    // Ordinal matches the historical behaviour of the inline directory cache this replaced.
    private readonly FreshDirectoryTracker _directoryTracker = new(StringComparison.Ordinal);
    public LocalFileSystemProvider(
        string rootPath,
        Func<string>? readLinuxMountInfo = null)
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
        : GetVolumeIdSafe(RootPath);

    private static string? GetVolumeIdSafe(string path)
    {
        try
        {
            string drivePath = OperatingSystem.IsWindows() 
                ? (Path.GetPathRoot(path) ?? path) 
                : path;
            return new DriveInfo(drivePath).Name;
        }
        catch
        {
            return Path.GetPathRoot(path)?.ToUpperInvariant();
        }
    }

    public ProviderCapabilities Capabilities => _capabilities;
    
    public ValueTask<Hardware.DriveClassification> GetClassificationAsync(CancellationToken ct = default)
    {
        if (_isNetworkPath)
            return ValueTask.FromResult(Hardware.DriveClassification.Unknown);
        return Hardware.DriveClassificationRegistry.GetOrClassifyAsync(RootPath, VolumeId, ct);
    }

    public StringComparer PathComparer => PathHelper.LocalPathComparer;

    public StringComparison PathComparison => PathHelper.LocalPathComparison;

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

    public Task<Stream> OpenReadAsync(string path, int? bufferSize = null, CancellationToken ct = default)
    {
        return Task.Run<Stream>(() =>
        {
            ct.ThrowIfCancellationRequested();
            var fullPath = Resolve(path);

            return new FileStream(
                fullPath,
                new FileStreamOptions
                {
                    Mode = FileMode.Open,
                    Access = FileAccess.Read,
                    Share = FileShare.Read,
                    BufferSize = bufferSize ?? DefaultBufferSize,
                    Options = FileOptions.Asynchronous | FileOptions.SequentialScan
                });
        }, ct);
    }

    public async Task WriteAsync(
        string path, 
        Stream data, 
        IProgress<long>? progress, 
        OperationalSettings? settings, 
        CancellationToken ct)
    {
        var opts = settings ?? new OperationalSettings();
        var fullPath = Resolve(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !_directoryTracker.IsKnown(directory))
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                _directoryTracker.MarkCreated(directory);
            }
            else
            {
                _directoryTracker.MarkKnown(directory);
            }
        }

        long? remainingBytes = StreamCopyEngine.TryGetRemainingLength(data, out var knownRemainingBytes)
            ? knownRemainingBytes
            : null;

        // Direct write (no staging): the strategy requests this for tiny files (where the write is
        // effectively atomic anyway) and the provider must obey. A partial file is best-effort
        // cleaned up on error.
        if (opts.WriteDurability == WriteDurability.Direct)
        {
            var fileOpened = false;
            try
            {
                await using (var output = new FileStream(
                    fullPath,
                    new FileStreamOptions
                    {
                        Mode = FileMode.Create,
                        Access = FileAccess.Write,
                        Share = FileShare.None,
                        BufferSize = opts.CopyBufferSizeBytes,
                        Options = FileOptions.Asynchronous
                    }))
                {
                    fileOpened = true;
                    await StreamCopyEngine.CopyAsync(data, output, remainingBytes, progress, opts, ct);
                }
            }
            catch
            {
                if (fileOpened)
                {
                    TryDeleteStagedFile(fullPath);
                }
                throw;
            }

            return;
        }

        // Staged write (atomic rename) — the crash-safe default.
        var tempPath = string.Empty;
        var stagedOutsideDestinationDirectory = false;
        var committed = false;
        try
        {
            await using (var output = CreateStagedWriteStream(
                fullPath,
                opts.CopyBufferSizeBytes,
                out tempPath,
                out stagedOutsideDestinationDirectory))
            {
                await StreamCopyEngine.CopyAsync(data, output, remainingBytes, progress, opts, ct);
            }

            CommitStagedWrite(tempPath, fullPath, stagedOutsideDestinationDirectory);
            committed = true;
        }
        finally
        {
            if (!committed && !string.IsNullOrWhiteSpace(tempPath))
            {
                TryDeleteStagedFile(tempPath);
            }
        }
    }

    private static string BuildStagedWritePath(string destinationPath)
    {
        var fileName = Path.GetFileName(destinationPath);
        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "smartcopy" : fileName;
        var maxSourceSegmentLength = Math.Max(
            0,
            MaxFileNameLength - StagedFilePrefix.Length - StagedFileSuffix.Length - GuidNLength);

        if (safeFileName.Length > maxSourceSegmentLength)
        {
            safeFileName = safeFileName[..maxSourceSegmentLength];
        }

        return Path.Combine(
            Path.GetDirectoryName(destinationPath) ?? Path.GetTempPath(),
            $"{StagedFilePrefix}{safeFileName}{StagedFileSuffix}{Guid.NewGuid():N}");
    }

    private static string BuildCompactStagedWritePath(string destinationPath)
    {
        return Path.Combine(
            Path.GetDirectoryName(destinationPath) ?? Path.GetTempPath(),
            $"{CompactStagedFilePrefix}{Guid.NewGuid():N}");
    }

    private static string BuildSystemTempStagedWritePath()
    {
        return Path.Combine(Path.GetTempPath(), $"{CompactStagedFilePrefix}{Guid.NewGuid():N}");
    }

    private static FileStream CreateStagedWriteStream(
        string destinationPath,
        int bufferSizeBytes,
        out string stagedPath,
        out bool stagedOutsideDestinationDirectory)
    {
        Exception? lastException = null;

        var candidate = BuildStagedWritePath(destinationPath);
        if (TryCreateStagedWriteStream(candidate, bufferSizeBytes, out var stream, out lastException))
        {
            stagedPath = candidate;
            stagedOutsideDestinationDirectory = false;
            return stream;
        }

        candidate = BuildCompactStagedWritePath(destinationPath);
        if (TryCreateStagedWriteStream(candidate, bufferSizeBytes, out stream, out var compactException))
        {
            stagedPath = candidate;
            stagedOutsideDestinationDirectory = false;
            return stream;
        }

        lastException = compactException ?? lastException;

        candidate = BuildSystemTempStagedWritePath();
        if (TryCreateStagedWriteStream(candidate, bufferSizeBytes, out stream, out var tempException))
        {
            stagedPath = candidate;
            stagedOutsideDestinationDirectory = true;
            return stream;
        }

        lastException = tempException ?? lastException;
        throw new IOException(
            $"Unable to create a staged file for destination '{destinationPath}'.",
            lastException);
    }

    private static bool TryCreateStagedWriteStream(
        string candidate,
        int bufferSizeBytes,
        out FileStream stream,
        out Exception? exception)
    {
        try
        {
            stream = new FileStream(
                candidate,
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    BufferSize = bufferSizeBytes,
                    Options = FileOptions.Asynchronous
                });

            exception = null;
            return true;
        }
        catch (PathTooLongException ex)
        {
            stream = null!;
            exception = ex;
            return false;
        }
        catch (DirectoryNotFoundException ex)
        {
            stream = null!;
            exception = ex;
            return false;
        }
        catch (IOException ex)
        {
            // CreateNew may race on name collisions; long paths may also surface as IO exceptions.
            stream = null!;
            exception = ex;
            return false;
        }
    }

    private static void CommitStagedWrite(string stagedPath, string destinationPath, bool stagedOutsideDestinationDirectory)
    {
        try
        {
            File.Move(stagedPath, destinationPath, overwrite: true);
        }
        catch (IOException) when (stagedOutsideDestinationDirectory)
        {
            // Cross-volume moves fail on many filesystems; fallback to copy+delete for temp-dir staging.
            File.Copy(stagedPath, destinationPath, overwrite: true);
            File.Delete(stagedPath);
        }
    }

    private static void TryDeleteStagedFile(string stagedPath)
    {
        try
        {
            if (File.Exists(stagedPath))
            {
                File.Delete(stagedPath);
            }
        }
        catch
        {
            // Best-effort cleanup only; preserve the original write failure.
        }
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
            // A directory we just created is empty by definition, so nothing inside it exists yet.
            if (_directoryTracker.IsFreshlyCreated(Path.GetDirectoryName(fullPath)))
                return false;
            return File.Exists(fullPath) || Directory.Exists(fullPath);
        }, ct);
    }

    public IAsyncDisposable BeginBulkWriteAsync()
    {
        _directoryTracker.Reset();
        return new BulkWriteScope(this);
    }

    private sealed class BulkWriteScope(LocalFileSystemProvider owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner._directoryTracker.Reset();
            return ValueTask.CompletedTask;
        }
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
