using MediaDevices;

namespace SmartCopy.Core.FileSystem;

/// <summary>
/// The small portion of a WPD device required by MTP deletion.
/// Kept separate from the full device API so silent device-side no-ops can be tested without hardware.
/// </summary>
internal interface IMtpDeleteDevice
{
    bool DirectoryExists(string path);
    bool FileExists(string path);
    void DeleteDirectory(string path, bool recursive);
    void DeleteFile(string path);
}

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class MediaDeviceDeleteAdapter(MediaDevice device) : IMtpDeleteDevice
{
    public bool DirectoryExists(string path) => device.DirectoryExists(path);
    public bool FileExists(string path) => device.FileExists(path);
    public void DeleteDirectory(string path, bool recursive) => device.DeleteDirectory(path, recursive);
    public void DeleteFile(string path) => device.DeleteFile(path);
}
