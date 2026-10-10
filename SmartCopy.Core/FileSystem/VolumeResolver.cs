namespace SmartCopy.Core.FileSystem;

/// <summary>
/// Resolves the volume a local path actually lives on — its identity and free space — following
/// junctions and mount points, so callers need not know how each OS answers. The per-OS mechanics live
/// in <see cref="WindowsVolumeResolver"/> and <see cref="MountPointResolver"/>.
/// <para>
/// A destination often does not exist yet, so queries that need a real folder start from its deepest
/// existing ancestor, which is on the volume it will be created on.
/// </para>
/// </summary>
internal static class VolumeResolver
{
    /// <summary>
    /// Whether <see cref="GetAvailableFreeSpace"/> works on network paths. Windows answers for UNC paths
    /// the same way it does for mapped drives; Unix network mounts are not trusted to report it.
    /// </summary>
    public static bool CanQueryNetworkFreeSpace => OperatingSystem.IsWindows();

    /// <summary>
    /// How long a resolved volume ID may be reused. On Windows resolving opens a handle — a network round
    /// trip on a mapped drive — and validation reads the ID several times per pass on the UI thread, so a
    /// short window keeps that cheap. On Unix it is a mount-table read and is never reused, so a device
    /// mounted or unmounted under a path is picked up immediately.
    /// </summary>
    public static long VolumeIdReuseMs => OperatingSystem.IsWindows() ? 2000 : 0;

    /// <summary>
    /// Identity of the volume holding <paramref name="path"/>: the volume GUID path (or share + volume
    /// serial on a mapped network drive) on Windows, the deepest containing mount point on Unix, or null
    /// when it cannot be determined.
    /// </summary>
    /// <param name="readMountPoints">Unix only: overrides the OS mount table, for tests.</param>
    public static string? GetVolumeId(string path, Func<IEnumerable<string>>? readMountPoints = null)
    {
        if (OperatingSystem.IsWindows())
        {
            return PathHelper.FindDeepestExistingDirectory(path) is { } existing
                ? WindowsVolumeResolver.GetVolumeId(existing)
                : null;
        }

        // On Unix `new DriveInfo(path).Name` echoes the path back rather than resolving the
        // containing mount point, so it must be matched against the mount table instead —
        // otherwise every folder looks like its own volume and no two paths ever compare equal.
        var mountPoints = readMountPoints?.Invoke();
        return mountPoints is null
            ? MountPointResolver.Resolve(path)
            : MountPointResolver.Resolve(path, mountPoints);
    }

    /// <summary>
    /// Free space available to the caller on the volume holding <paramref name="path"/>, or null when
    /// no ancestor exists. Blocks — on a network path for as long as the server takes to answer.
    /// </summary>
    public static long? GetAvailableFreeSpace(string path)
    {
        if (PathHelper.FindDeepestExistingDirectory(path) is not { } existing)
            return null;

        // DriveInfo truncates a Windows path to its drive root (and rejects UNC paths), which misses
        // junctions and mount points; on Unix it statfs's the path as given, which follows them.
        return OperatingSystem.IsWindows()
            ? WindowsVolumeResolver.GetAvailableFreeSpace(existing)
            : new DriveInfo(existing).AvailableFreeSpace;
    }
}
