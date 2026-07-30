namespace SmartCopy.Core.FileSystem;

/// <summary>
/// Resolves which mounted volume contains a POSIX path.
/// <para>
/// .NET's <c>DriveInfo(string)</c> constructor does not resolve to the containing mount point on Unix —
/// it echoes the path back, so <c>new DriveInfo("/Users/alice").Name</c> is <c>"/Users/alice"</c> rather
/// than <c>"/"</c>. Volume identity therefore has to be derived by matching the path against the mounted
/// volume list, taking the <em>deepest</em> mount point that contains it: nested mounts (an external disk
/// under <c>/Volumes</c>, a FUSE filesystem inside a home directory) must win over their parent.
/// </para>
/// <para>
/// Paths are treated as POSIX paths and compared ordinally. This helper is not used on Windows, where
/// <see cref="Path.GetPathRoot(string?)"/> already yields the volume.
/// </para>
/// <para>
/// <b>macOS firmlink caveat.</b> Paths under <c>/Users</c> resolve to "/" by prefix even though their
/// data lives on <c>/System/Volumes/Data</c>, so a path on the read-only system volume and a path in a
/// home directory share an ID despite being separate APFS volumes. That is accepted: it only affects
/// buffer routing, and the one operation that acts on a same-volume claim — <c>MoveStep</c>'s atomic
/// rename — degrades safely. A cross-volume <c>Directory.Move</c> raises <c>IOException("Cross-device
/// link")</c>, which <c>MoveStep</c> catches to fall back to a piecewise walk, and <c>File.Move</c>
/// handles EXDEV internally by copying then deleting (both verified on macOS 15 / .NET 10).
/// </para>
/// </summary>
internal static class MountPointResolver
{
    /// <summary>
    /// Returns the deepest mount point currently reported by the OS that contains <paramref name="path"/>,
    /// or <see langword="null"/> when nothing matches or the mount table cannot be read.
    /// </summary>
    public static string? Resolve(string path) => Resolve(path, EnumerateSystemMountPoints());

    /// <summary>
    /// Returns the deepest entry of <paramref name="mountPoints"/> that contains (or equals)
    /// <paramref name="path"/>, or <see langword="null"/> when none does.
    /// </summary>
    public static string? Resolve(string path, IEnumerable<string> mountPoints)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var normalizedPath = NormalizePosixPath(path);
        string? best = null;

        foreach (var mountPoint in mountPoints)
        {
            if (string.IsNullOrWhiteSpace(mountPoint))
            {
                continue;
            }

            var normalizedMount = NormalizePosixPath(mountPoint);

            if (!Contains(normalizedMount, normalizedPath))
            {
                continue;
            }

            // Deepest wins: "/Volumes/External" must beat "/" for a path on the external disk.
            if (best is not null && normalizedMount.Length <= best.Length)
            {
                continue;
            }

            best = normalizedMount;
        }

        return best;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="path"/> is <paramref name="mountPoint"/> itself
    /// or lives beneath it. Both arguments must already be normalized. The match is anchored to a segment
    /// boundary so "/Volumes/Data" does not claim "/Volumes/DataBackup".
    /// </summary>
    public static bool Contains(string mountPoint, string path)
    {
        if (string.Equals(path, mountPoint, StringComparison.Ordinal))
        {
            return true;
        }

        if (mountPoint == "/")
        {
            return path.StartsWith('/');
        }

        return path.Length > mountPoint.Length
            && path.StartsWith(mountPoint, StringComparison.Ordinal)
            && path[mountPoint.Length] == '/';
    }

    /// <summary>
    /// Collapses duplicate separators and strips trailing separators, preserving the "/" root.
    /// <para>
    /// Whitespace is significant and is never trimmed: it is legal anywhere in a Unix path component,
    /// including at the end of a volume name. Trimming it would leave a mount at "/Volumes/Archive "
    /// unable to match its own contents, silently attributing them to the parent volume.
    /// </para>
    /// </summary>
    private static string NormalizePosixPath(string path)
    {
        var normalized = path;

        while (normalized.Contains("//", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
        }

        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

    /// <summary>
    /// The OS mount table. <see cref="DriveInfo.GetDrives"/> is the only mount enumeration in the BCL that
    /// works on both macOS (getmntinfo) and Linux (/proc/mounts); an unreadable table yields no mount
    /// points rather than an exception, leaving the caller to fall back.
    /// </summary>
    private static IEnumerable<string> EnumerateSystemMountPoints()
    {
        try
        {
            return DriveInfo.GetDrives().Select(drive => drive.Name).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
