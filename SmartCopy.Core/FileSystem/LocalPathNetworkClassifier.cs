namespace SmartCopy.Core.FileSystem;

internal static class LocalPathNetworkClassifier
{
    private static readonly HashSet<string> LinuxRemoteFileSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "9p",
        "afs",
        "ceph",
        "cephfs",
        "cifs",
        "davfs",
        "davfs2",
        "fuse.davfs",
        "fuse.gcsfuse",
        "fuse.rclone",
        "fuse.sshfs",
        "glusterfs",
        "gcsfuse",
        "ncp",
        "ncpfs",
        "nfs",
        "nfs4",
        "rclone",
        "smb3",
        "smbfs",
        "sshfs",
    };

    public static bool IsNetworkPath(string normalizedPath, Func<string>? readLinuxMountInfo = null)
    {
        if (IsWindowsUncPath(normalizedPath))
        {
            return true;
        }

        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        return IsLinuxRemoteMountPath(normalizedPath, readLinuxMountInfo);
    }

    private static bool IsWindowsUncPath(string normalizedPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        if (normalizedPath.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return true;
        }

        return Uri.TryCreate(normalizedPath, UriKind.Absolute, out var uri) && uri.IsUnc;
    }

    private static bool IsLinuxRemoteMountPath(string normalizedPath, Func<string>? readLinuxMountInfo)
    {
        string mountInfo;
        try
        {
            mountInfo = readLinuxMountInfo?.Invoke() ?? File.ReadAllText("/proc/self/mountinfo");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return TryGetLinuxMountEntryForPath(mountInfo, normalizedPath, out var fsType)
            && LinuxRemoteFileSystems.Contains(fsType);
    }

    private static bool TryGetLinuxMountEntryForPath(string mountInfo, string path, out string fsType)
    {
        fsType = string.Empty;
        var normalizedPath = NormalizeLinuxPath(path);

        string bestMountPoint = string.Empty;
        string bestFsType = string.Empty;

        foreach (var line in mountInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseMountInfoLine(line, out var mountPoint, out var lineFsType))
            {
                continue;
            }

            if (!IsSamePathOrChild(normalizedPath, mountPoint))
            {
                continue;
            }

            if (mountPoint.Length < bestMountPoint.Length)
            {
                continue;
            }

            bestMountPoint = mountPoint;
            bestFsType = lineFsType;
        }

        if (string.IsNullOrEmpty(bestFsType))
        {
            return false;
        }

        fsType = bestFsType;
        return true;
    }

    private static bool TryParseMountInfoLine(string line, out string mountPoint, out string fsType)
    {
        mountPoint = string.Empty;
        fsType = string.Empty;

        var separatorIndex = line.IndexOf(" - ", StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            return false;
        }

        var preSeparator = line[..separatorIndex];
        var postSeparator = line[(separatorIndex + 3)..];

        var preFields = preSeparator.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (preFields.Length < 5)
        {
            return false;
        }

        var postFields = postSeparator.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (postFields.Length < 2)
        {
            return false;
        }

        mountPoint = NormalizeLinuxPath(UnescapeMountInfoField(preFields[4]));
        fsType = postFields[0];
        return true;
    }

    private static string NormalizeLinuxPath(string path)
    {
        var normalized = Path.GetFullPath(path);
        if (normalized.Length > 1)
        {
            normalized = normalized.TrimEnd('/');
        }

        return normalized;
    }

    private static bool IsSamePathOrChild(string path, string mountPoint)
    {
        if (string.Equals(path, mountPoint, StringComparison.Ordinal))
        {
            return true;
        }

        if (mountPoint == "/")
        {
            return path.StartsWith("/", StringComparison.Ordinal);
        }

        return path.Length > mountPoint.Length
            && path.StartsWith(mountPoint, StringComparison.Ordinal)
            && path[mountPoint.Length] == '/';
    }

    private static string UnescapeMountInfoField(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\'
                && i + 3 < value.Length
                && IsOctalDigit(value[i + 1])
                && IsOctalDigit(value[i + 2])
                && IsOctalDigit(value[i + 3]))
            {
                var code =
                    (value[i + 1] - '0') * 64 +
                    (value[i + 2] - '0') * 8 +
                    (value[i + 3] - '0');
                builder.Append((char)code);
                i += 3;
                continue;
            }

            builder.Append(value[i]);
        }

        return builder.ToString();
    }

    private static bool IsOctalDigit(char c) => c is >= '0' and <= '7';
}
