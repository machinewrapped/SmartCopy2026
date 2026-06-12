namespace SmartCopy.Core.FileSystem.Hardware;

internal sealed class LinuxDriveClassifier : IDriveClassifier
{
    public Task<DriveClassification> ClassifyAsync(string rootPath, CancellationToken ct = default)
    {
        return Task.Run(() => ClassifyInternal(rootPath), ct);
    }

    private DriveClassification ClassifyInternal(string rootPath)
    {
        string mountSource = GetMountSourceForPath(rootPath);
        if (string.IsNullOrEmpty(mountSource) || !mountSource.StartsWith("/dev/", StringComparison.Ordinal))
        {
            return DriveClassification.Unknown;
        }

        if (File.Exists(mountSource))
        {
            try
            {
                var resolved = File.ResolveLinkTarget(mountSource, true);
                if (resolved != null) mountSource = resolved.FullName;
            }
            catch { }
        }

        string devName = Path.GetFileName(mountSource);
        string linkPath = $"/sys/class/block/{devName}";
        
        if (!Directory.Exists(linkPath))
        {
            return DriveClassification.Unknown;
        }

        try
        {
            var resolvedInfo = Directory.ResolveLinkTarget(linkPath, true);
            if (resolvedInfo == null)
                return DriveClassification.Unknown;

            string realPath = resolvedInfo.FullName;
            
            DriveMediaType mediaType = DriveMediaType.Unknown;
            string rotationalPath = Path.Combine(realPath, "queue", "rotational");
            if (!File.Exists(rotationalPath))
            {
                string? parentDir = Path.GetDirectoryName(realPath);
                if (parentDir != null)
                {
                    rotationalPath = Path.Combine(parentDir, "queue", "rotational");
                }
            }

            if (File.Exists(rotationalPath))
            {
                string content = File.ReadAllText(rotationalPath).Trim();
                if (content == "0") mediaType = DriveMediaType.SSD;
                else if (content == "1") mediaType = DriveMediaType.HDD;
            }

            DriveInterfaceType interfaceType = DriveInterfaceType.Unknown;
            string lowerPath = realPath.ToLowerInvariant();
            if (lowerPath.Contains("/nvme"))
                interfaceType = DriveInterfaceType.NVMe;
            else if (lowerPath.Contains("/usb"))
                interfaceType = DriveInterfaceType.USB;
            else if (lowerPath.Contains("/ata") || lowerPath.Contains("/scsi"))
                interfaceType = DriveInterfaceType.SATA;

            return new DriveClassification(mediaType, interfaceType);
        }
        catch
        {
            return DriveClassification.Unknown;
        }
    }

    private static string GetMountSourceForPath(string path)
    {
        try
        {
            string mountInfo = File.ReadAllText("/proc/self/mountinfo");
            var normalizedPath = NormalizeLinuxPath(path);
            
            string bestMountPoint = string.Empty;
            string bestSource = string.Empty;

            foreach (var line in mountInfo.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separatorIndex = line.IndexOf(" - ", StringComparison.Ordinal);
                if (separatorIndex <= 0) continue;

                var preSeparator = line[..separatorIndex];
                var postSeparator = line[(separatorIndex + 3)..];

                var preFields = preSeparator.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (preFields.Length < 5) continue;

                var postFields = postSeparator.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (postFields.Length < 2) continue;

                var mountPoint = NormalizeLinuxPath(UnescapeMountInfoField(preFields[4]));
                var source = UnescapeMountInfoField(postFields[1]);

                if (!IsSamePathOrChild(normalizedPath, mountPoint))
                    continue;

                if (mountPoint.Length > bestMountPoint.Length)
                {
                    bestMountPoint = mountPoint;
                    bestSource = source;
                }
            }
            return bestSource;
        }
        catch
        {
            return string.Empty;
        }
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
            return true;

        if (mountPoint == "/")
            return path.StartsWith("/", StringComparison.Ordinal);

        return path.Length > mountPoint.Length
            && path.StartsWith(mountPoint, StringComparison.Ordinal)
            && path[mountPoint.Length] == '/';
    }

    private static string UnescapeMountInfoField(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
            return value;

        var builder = new System.Text.StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 3 < value.Length)
            {
                var c1 = value[i + 1];
                var c2 = value[i + 2];
                var c3 = value[i + 3];
                if (c1 is >= '0' and <= '7' && c2 is >= '0' and <= '7' && c3 is >= '0' and <= '7')
                {
                    int code = (c1 - '0') * 64 + (c2 - '0') * 8 + (c3 - '0');
                    builder.Append((char)code);
                    i += 3;
                    continue;
                }
            }
            builder.Append(value[i]);
        }
        return builder.ToString();
    }
}
