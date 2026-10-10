using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace SmartCopy.Core.FileSystem;

/// <summary>
/// Resolves the volume a Windows folder actually lives on, following junctions and volume mount points —
/// including junctions on an SMB server, which the server resolves for the handle.
/// <para>
/// <see cref="DriveInfo"/> and <see cref="Path.GetPathRoot(string?)"/> only see the drive root, so a folder
/// that is a junction or mount point onto another volume (<c>C:\Media\Disk2</c>, or a share whose top-level
/// folders are junctions on the server) is attributed to the root's volume. Both queries here work on the
/// folder itself, so reparse points are resolved first.
/// </para>
/// <para>
/// Callers pass an existing directory: a destination often does not exist yet, and its deepest existing
/// ancestor is on the same volume.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsVolumeResolver
{
    private const uint FILE_READ_ATTRIBUTES = 0x0080;
    private const uint FILE_SHARE_ALL = 0x00000001 | 0x00000002 | 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint VOLUME_NAME_DOS = 0x0;
    private const uint VOLUME_NAME_GUID = 0x1;

    private const string GuidVolumePrefix = @"\\?\Volume{";
    private const string UncFinalPathPrefix = @"\\?\UNC\";
    private const string LocalFinalPathPrefix = @"\\?\";

    /// <summary>Free space available to the caller on the volume holding <paramref name="directory"/>.</summary>
    public static long? GetAvailableFreeSpace(string directory) =>
        GetDiskFreeSpaceExW(PathHelper.EnsureTrailingSeparator(directory), out var available, out _, out _)
            ? (long)available
            : null;

    /// <summary>
    /// Identity of the volume holding <paramref name="directory"/>, or null when it cannot be opened.
    /// <list type="bullet">
    /// <item>Local volumes: the volume GUID path (<c>\\?\Volume{…}\</c>), which is unique and the same
    /// whether the volume is reached by drive letter, mount point or junction.</item>
    /// <item>Remote volumes have no GUID path. The volume serial number tells the server's volumes apart;
    /// it is qualified with the share (<c>\\server\share|1A2B3C4D</c>) so equal serials on different
    /// machines cannot collide, and so two shares never claim one volume — SMB cannot rename across
    /// shares.</item>
    /// </list>
    /// </summary>
    public static string? GetVolumeId(string directory)
    {
        using var handle = CreateFileW(
            directory,
            FILE_READ_ATTRIBUTES,
            FILE_SHARE_ALL,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (handle.IsInvalid) return null;

        if (GetFinalPath(handle, VOLUME_NAME_GUID) is { } guidPath && VolumeIdFromGuidPath(guidPath) is { } guidId)
            return guidId;

        if (!GetFileInformationByHandle(handle, out var info)) return null;

        var root = GetFinalPath(handle, VOLUME_NAME_DOS) is { } dosPath
            ? RootFromFinalPath(dosPath)
            : null;

        return RemoteVolumeId(root ?? Path.GetPathRoot(directory) ?? directory, info.VolumeSerialNumber);
    }

    /// <summary>
    /// Device path of the local volume holding <paramref name="directory"/> (<c>\\?\Volume{guid}</c>, no
    /// trailing separator, so <c>CreateFile</c> opens the volume rather than its root folder), or null for
    /// remote volumes or when it cannot be opened.
    /// </summary>
    public static string? GetLocalVolumeDevicePath(string directory) =>
        GetVolumeId(directory) is { } id && id.StartsWith(GuidVolumePrefix, StringComparison.OrdinalIgnoreCase)
            ? id.TrimEnd('\\')
            : null;

    /// <summary><c>\\?\Volume{guid}\Users</c> → <c>\\?\Volume{guid}\</c>.</summary>
    internal static string? VolumeIdFromGuidPath(string finalPath)
    {
        if (!finalPath.StartsWith(GuidVolumePrefix, StringComparison.OrdinalIgnoreCase)) return null;

        var end = finalPath.IndexOf(@"}\", GuidVolumePrefix.Length, StringComparison.Ordinal);
        return end < 0 ? null : finalPath[..(end + 2)];
    }

    /// <summary><c>\\?\UNC\server\share\dir</c> → <c>\\server\share</c>; <c>\\?\C:\dir</c> → <c>C:\</c>.</summary>
    internal static string? RootFromFinalPath(string finalPath)
    {
        string path;
        if (finalPath.StartsWith(UncFinalPathPrefix, StringComparison.OrdinalIgnoreCase))
            path = @"\\" + finalPath[UncFinalPathPrefix.Length..];
        else if (finalPath.StartsWith(LocalFinalPathPrefix, StringComparison.Ordinal))
            path = finalPath[LocalFinalPathPrefix.Length..];
        else
            path = finalPath;

        return Path.GetPathRoot(path) is { Length: > 0 } root ? root : null;
    }

    internal static string RemoteVolumeId(string root, uint volumeSerialNumber) =>
        $"{root}|{volumeSerialNumber:X8}";

    private static string? GetFinalPath(SafeFileHandle handle, uint flags)
    {
        var buffer = new StringBuilder(1024);
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, flags);
        if (length == 0) return null;

        if (length > buffer.Capacity)
        {
            buffer.EnsureCapacity((int)length);
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, flags);
            if (length == 0 || length > buffer.Capacity) return null;
        }

        return buffer.ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out BY_HANDLE_FILE_INFORMATION lpFileInformation);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailable,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);
}
