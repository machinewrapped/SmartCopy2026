using System.Runtime.Versioning;
using MediaDevices;

namespace SmartCopy.Core.FileSystem;

[SupportedOSPlatform("windows")]
public static class MtpProviderFactory
{
    /// <summary>
    /// Attempts to find the MTP device named in <paramref name="mtpPath"/> among currently
    /// connected devices and create a provider rooted at that path.
    /// Returns <see langword="null"/> if no matching device is connected.
    /// </summary>
    public static IFileSystemProvider? Create(string mtpPath)
    {
        var deviceName = ParseDeviceName(mtpPath);
        if (deviceName is null) return null;

        var device = MediaDevice.GetDevices()
            .FirstOrDefault(d => NameMatches(d, deviceName));
        if (device is null) return null;

        return new MtpFileSystemProvider(device, mtpPath);
    }

    /// <summary>Extracts the device name from an <c>mtp://DeviceName/...</c> path.</summary>
    internal static string? ParseDeviceName(string mtpPath)
    {
        const string prefix = "mtp://";
        if (!mtpPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;
        var rest = mtpPath[prefix.Length..];
        var slash = rest.IndexOf('/');
        return slash < 0 ? rest : rest[..slash];
    }

    private static bool NameMatches(MediaDevice device, string name)
    {
        var deviceName = string.IsNullOrEmpty(device.FriendlyName) ? device.Model : device.FriendlyName;
        return string.Equals(deviceName, name, StringComparison.OrdinalIgnoreCase);
    }
}
