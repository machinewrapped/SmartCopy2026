using System.Diagnostics;
using System.Xml.Linq;

namespace SmartCopy.Core.FileSystem.Hardware;

internal sealed class MacDriveClassifier : IDriveClassifier
{
    /// <summary>Absolute path avoids depending on the PATH of a GUI-launched process.</summary>
    private const string DiskUtilPath = "/usr/sbin/diskutil";

    private const string MountPath = "/sbin/mount";

    /// <summary>Generous: a sleeping external drive can take seconds to answer diskutil.</summary>
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);

    public async Task<DriveClassification> ClassifyAsync(string rootPath, CancellationToken ct = default)
    {
        // diskutil only accepts a device node or a mount point; an arbitrary directory below the
        // mount point ("/Volumes/PortableSSD/TestData") exits 1 with "Could not find disk".
        string? mountTable = await RunAsync(MountPath, [], ct).ConfigureAwait(false);
        if (mountTable is null) return DriveClassification.Unknown;

        var mount = ResolveMount(rootPath, ParseMountTable(mountTable));
        if (mount is not { } volume) return DriveClassification.Unknown;

        // Queried by device node, not mount point: the mount-point form makes diskutil stat the
        // filesystem, measured at 0.6-3.6s for an idle external drive against ~0.2s by device.
        string target = volume.DeviceIdentifier ?? volume.MountPoint;

        string? plist = await RunDiskUtilAsync(target, ct).ConfigureAwait(false);
        if (plist is null) return DriveClassification.Unknown;

        var info = ParseInfo(plist);
        if (info.Classification.MediaType != DriveMediaType.Unknown) return info.Classification;

        var mediaType = await ResolveExternalMediaTypeAsync(info, rootPath, volume.MountPoint, ct)
            .ConfigureAwait(false);
        return info.Classification with { MediaType = mediaType };
    }

    /// <summary>
    /// Recovers the media type for a drive whose volume record does not state it — in practice
    /// anything behind a USB bridge, which does not pass the rotation rate to the host. The whole-disk
    /// record is consulted first because it is authoritative when present and names the product
    /// otherwise; only then does the latency probe run.
    /// </summary>
    private static async Task<DriveMediaType> ResolveExternalMediaTypeAsync(
        DiskUtilInfo volumeInfo, string rootPath, string mountPoint, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(volumeInfo.ParentWholeDisk))
        {
            string? diskPlist = await RunDiskUtilAsync(volumeInfo.ParentWholeDisk, ct).ConfigureAwait(false);
            if (diskPlist is not null)
            {
                var diskInfo = ParseInfo(diskPlist);
                if (diskInfo.Classification.MediaType != DriveMediaType.Unknown)
                    return diskInfo.Classification.MediaType;

                var byName = MediaTypeProbe.FromDeviceName(diskInfo.DeviceName);
                if (byName != DriveMediaType.Unknown) return byName;
            }
        }

        // The scanned folder is searched first: it is on the same volume and far likelier to hold
        // sample files than the mount root, which may be a shallow directory of empty folders.
        string[] searchRoots = string.Equals(rootPath, mountPoint, StringComparison.Ordinal)
            ? [mountPoint]
            : [rootPath, mountPoint];

        return await MediaTypeProbe.MeasureAsync(mountPoint, searchRoots, ct).ConfigureAwait(false);
    }

    /// <summary>The subset of a <c>diskutil info</c> record this classifier reads.</summary>
    internal readonly record struct DiskUtilInfo(
        DriveClassification Classification,
        string? DeviceName,
        string? ParentWholeDisk);

    /// <param name="target">A mount point or a device identifier such as "disk4".</param>
    private static Task<string?> RunDiskUtilAsync(string target, CancellationToken ct) =>
        RunAsync(DiskUtilPath, ["info", "-plist", target], ct);

    /// <summary>
    /// Runs a system tool and returns its stdout, or null if it fails, times out or writes nothing.
    /// </summary>
    /// <param name="toolPath">Absolute, so a GUI-launched process's PATH does not matter.</param>
    private static async Task<string?> RunAsync(string toolPath, string[] arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = File.Exists(toolPath) ? toolPath : Path.GetFileName(toolPath),
            RedirectStandardOutput = true,
            RedirectStandardError = false,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) psi.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(psi);
            if (process == null) return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProcessTimeout);

            var readTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }

            string output = await readTask.ConfigureAwait(false);
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a <c>diskutil info -plist</c> document. Keys and values are siblings in the top-level
    /// dict, so values are attributed to the preceding key.
    /// </summary>
    internal static DiskUtilInfo ParseInfo(string plistXml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(plistXml);
        }
        catch (System.Xml.XmlException)
        {
            return Unreadable;
        }

        var dict = doc.Element("plist")?.Element("dict");
        if (dict == null) return Unreadable;

        DriveMediaType mediaType = DriveMediaType.Unknown;
        DriveInterfaceType interfaceType = DriveInterfaceType.Unknown;
        string? deviceName = null;
        string? parentWholeDisk = null;

        string? currentKey = null;
        foreach (var element in dict.Elements())
        {
            if (element.Name == "key")
            {
                currentKey = element.Value;
                continue;
            }

            if (currentKey == null) continue;

            switch (currentKey)
            {
                // diskutil reports failures in-band as <key>Error</key><true/>.
                case "Error" when element.Name.LocalName == "true":
                    return Unreadable;

                // Absent for USB bridges, which do not expose rotation to the host. Left Unknown here
                // rather than guessed at; ResolveExternalMediaTypeAsync recovers it.
                case "SolidState":
                    mediaType = element.Name.LocalName == "true" ? DriveMediaType.SSD : DriveMediaType.HDD;
                    break;

                case "BusProtocol":
                    interfaceType = ParseBusProtocol(element.Value);
                    break;

                // Both name the hardware, and both are empty on a volume record; the whole-disk record
                // carries them. IORegistryEntryName includes the vendor, so it is preferred.
                case "IORegistryEntryName":
                    deviceName = NullIfBlank(element.Value);
                    break;

                case "MediaName":
                    deviceName ??= NullIfBlank(element.Value);
                    break;

                case "ParentWholeDisk":
                    parentWholeDisk = NullIfBlank(element.Value);
                    break;
            }

            currentKey = null;
        }

        return new DiskUtilInfo(new DriveClassification(mediaType, interfaceType), deviceName, parentWholeDisk);
    }

    private static readonly DiskUtilInfo Unreadable = new(DriveClassification.Unknown, null, null);

    private static string? NullIfBlank(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static DriveInterfaceType ParseBusProtocol(string value)
    {
        // "PCI-Express" on Intel Macs; "Apple Fabric" is the Apple Silicon internal NVMe bus.
        if (value.Contains("PCI", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Apple Fabric", StringComparison.OrdinalIgnoreCase))
            return DriveInterfaceType.NVMe;
        if (value.Contains("SATA", StringComparison.OrdinalIgnoreCase))
            return DriveInterfaceType.SATA;
        if (value.Contains("USB", StringComparison.OrdinalIgnoreCase))
            return DriveInterfaceType.USB;
        if (value.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Disk Image", StringComparison.OrdinalIgnoreCase))
            return DriveInterfaceType.Virtual;
        return DriveInterfaceType.Unknown;
    }

    /// <summary>A mounted volume as reported by <c>mount(8)</c>.</summary>
    internal readonly record struct MountEntry(string Device, string MountPoint)
    {
        /// <summary>
        /// The name diskutil accepts ("disk5s1"), or null for a volume with no block device behind
        /// it — autofs maps and userspace filesystems, which diskutil cannot describe anyway.
        /// </summary>
        public string? DeviceIdentifier =>
            Device.StartsWith("/dev/", StringComparison.Ordinal) ? Device["/dev/".Length..] : null;
    }

    /// <summary>
    /// Parses <c>mount(8)</c> output, whose lines read "<c>device on /mount/point (options)</c>".
    /// A mount point may contain spaces — including trailing ones, which are significant — so the
    /// options suffix is located from the end and lines are only trimmed of carriage returns.
    /// </summary>
    internal static List<MountEntry> ParseMountTable(string mountOutput)
    {
        const string Separator = " on ";
        var entries = new List<MountEntry>();

        foreach (var rawLine in mountOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd('\r');

            int separator = line.IndexOf(Separator, StringComparison.Ordinal);
            if (separator <= 0) continue;

            var remainder = line[(separator + Separator.Length)..];
            int options = remainder.LastIndexOf(" (", StringComparison.Ordinal);
            var mountPoint = options > 0 ? remainder[..options] : remainder;
            if (mountPoint.Length == 0) continue;

            entries.Add(new MountEntry(line[..separator], MountPointResolver.NormalizePosixPath(mountPoint)));
        }

        return entries;
    }

    /// <summary>
    /// Returns the deepest mounted volume containing <paramref name="path"/>, or null when none does.
    /// Delegates path matching to <see cref="MountPointResolver"/> so the classifier and volume
    /// identity agree on what "contains" means; this adds only the device node, which
    /// <see cref="MountPointResolver"/> does not carry.
    /// </summary>
    internal static MountEntry? ResolveMount(string path, IEnumerable<MountEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string normalizedPath = MountPointResolver.NormalizePosixPath(path);
        MountEntry? best = null;

        foreach (var entry in entries)
        {
            string mountPoint = MountPointResolver.NormalizePosixPath(entry.MountPoint);
            if (mountPoint.Length == 0 || !MountPointResolver.Contains(mountPoint, normalizedPath)) continue;

            if (best is null || mountPoint.Length > best.Value.MountPoint.Length)
                best = entry with { MountPoint = mountPoint };
        }

        return best;
    }
}
