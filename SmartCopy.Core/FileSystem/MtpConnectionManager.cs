using System.Runtime.Versioning;
using MediaDevices;

namespace SmartCopy.Core.FileSystem;

/// <summary>
/// Manages MTP device connections with reference counting. Multiple <see cref="MtpFileSystemProvider"/>
/// instances may share the same physical device; the manager ensures only one <see cref="MediaDevice"/>
/// instance is connected per device and tracks which providers are using it.
/// </summary>
[SupportedOSPlatform("windows")]
public static class MtpConnectionManager
{
    private sealed class Connection
    {
        public required MediaDevice Device { get; init; }
        public List<MtpFileSystemProvider> Providers { get; } = [];
    }

    private static readonly System.Threading.Lock Sync = new();
    private static readonly Dictionary<string, Connection> Connections = new();

    /// <summary>
    /// Returns a connected <see cref="MediaDevice"/> for the same physical device.
    /// If one is already connected, returns that instance; otherwise connects the provided one.
    /// The <paramref name="provider"/> is tracked so it can be notified on external disconnect (future).
    /// </summary>
    public static MediaDevice Acquire(MediaDevice device, MtpFileSystemProvider provider)
    {
        lock (Sync)
        {
            var id = device.DeviceId;
            if (Connections.TryGetValue(id, out var conn))
            {
                conn.Providers.Add(provider);
                return conn.Device;
            }

            device.Connect();
            Connections[id] = new Connection { Device = device, Providers = { provider } };
            return device;
        }
    }

    /// <summary>
    /// Removes <paramref name="provider"/> from the connection's provider list.
    /// Disconnects the device when no providers remain.
    /// </summary>
    public static void Release(MtpFileSystemProvider provider)
    {
        lock (Sync)
        {
            foreach (var (id, conn) in Connections)
            {
                if (!conn.Providers.Remove(provider)) continue;

                if (conn.Providers.Count == 0)
                {
                    Connections.Remove(id);
                    conn.Device.Disconnect();
                }
                return;
            }
        }
    }
}
