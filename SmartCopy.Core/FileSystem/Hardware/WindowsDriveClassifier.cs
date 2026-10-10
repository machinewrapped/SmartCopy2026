using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace SmartCopy.Core.FileSystem.Hardware;

[SupportedOSPlatform("windows")]
internal sealed class WindowsDriveClassifier : IDriveClassifier
{
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_PROPERTY_QUERY
    {
        public uint PropertyId;
        public uint QueryType;
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
    {
        public uint Version;
        public uint Size;
        [MarshalAs(UnmanagedType.U1)]
        public bool IncursSeekPenalty;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STORAGE_DEVICE_DESCRIPTOR
    {
        public uint Version;
        public uint Size;
        public byte DeviceType;
        public byte DeviceTypeModifier;
        [MarshalAs(UnmanagedType.U1)]
        public bool RemovableMedia;
        [MarshalAs(UnmanagedType.U1)]
        public bool CommandQueueing;
        public uint VendorIdOffset;
        public uint ProductIdOffset;
        public uint ProductRevisionOffset;
        public uint SerialNumberOffset;
        public uint BusType; 
        public uint RawPropertiesLength;
    }

    private enum StorageBusType : uint
    {
        Unknown = 0x00,
        Scsi = 0x01,
        Atapi = 0x02,
        Ata = 0x03,
        FireWire = 0x04,
        Ssa = 0x05,
        Fibre = 0x06,
        Usb = 0x07,
        Raid = 0x08,
        iScsi = 0x09,
        Sas = 0x0A,
        Sata = 0x0B,
        Sd = 0x0C,
        Mmc = 0x0D,
        Virtual = 0x0E,
        FileBackedVirtual = 0x0F,
        Spaces = 0x10,
        Nvme = 0x11,
        Scm = 0x12,
        Ufs = 0x13
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

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref STORAGE_PROPERTY_QUERY lpInBuffer,
        int nInBufferSize,
        ref DEVICE_SEEK_PENALTY_DESCRIPTOR lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref STORAGE_PROPERTY_QUERY lpInBuffer,
        int nInBufferSize,
        ref STORAGE_DEVICE_DESCRIPTOR lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref STORAGE_PROPERTY_QUERY lpInBuffer,
        int nInBufferSize,
        IntPtr lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    public Task<DriveClassification> ClassifyAsync(string rootPath, CancellationToken ct = default)
    {
        return Task.Run(() => ClassifyInternal(rootPath), ct);
    }

    private DriveClassification ClassifyInternal(string rootPath)
    {
        // A mapped network drive is not a UNC path, so the provider treats it as local; the storage
        // query cannot see through the redirector, so identify it here instead.
        if (IsNetworkDrive(rootPath))
            return DriveClassification.Network;

        // Query the volume rootPath actually lives on, so a junction or mount point onto another disk is
        // classified as that disk. This must agree with LocalFileSystemProvider.VolumeId, which keys the
        // classification cache — otherwise the first path classified decides the result for its volume.
        string devicePath;
        if (PathHelper.FindDeepestExistingDirectory(rootPath) is { } existing
            && WindowsVolumeResolver.GetVolumeId(existing) is { } volumeId)
        {
            // A folder on a local drive can still resolve to a share (a directory symlink to a UNC path).
            if (WindowsVolumeResolver.LocalVolumeDevicePath(volumeId) is not { } volumeDevicePath)
                return DriveClassification.Network;

            devicePath = volumeDevicePath;
        }
        else if (Path.GetPathRoot(rootPath) is { Length: > 0 } root)
        {
            // The volume could not be resolved; fall back to the drive the path is written under.
            devicePath = $@"\\.\{root.TrimEnd('\\')}";
        }
        else
        {
            return DriveClassification.Unknown;
        }

        using SafeFileHandle handle = CreateFileW(
            devicePath,
            0,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero,
            OPEN_EXISTING,
            FILE_FLAG_BACKUP_SEMANTICS,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return DriveClassification.Unknown;
        }

        DriveMediaType mediaType = DriveMediaType.Unknown;
        DriveInterfaceType interfaceType = DriveInterfaceType.Unknown;

        var query = new STORAGE_PROPERTY_QUERY
        {
            PropertyId = 7, // StorageDeviceSeekPenaltyProperty
            QueryType = 0,  // PropertyStandardQuery
            AdditionalParameters = 0
        };

        var seekPenaltyDesc = new DEVICE_SEEK_PENALTY_DESCRIPTOR();
        bool success = DeviceIoControl(
            handle,
            IOCTL_STORAGE_QUERY_PROPERTY,
            ref query,
            Marshal.SizeOf(query),
            ref seekPenaltyDesc,
            Marshal.SizeOf(seekPenaltyDesc),
            out _,
            IntPtr.Zero);

        if (success)
        {
            mediaType = seekPenaltyDesc.IncursSeekPenalty ? DriveMediaType.HDD : DriveMediaType.SSD;
        }

        query.PropertyId = 0; // StorageDeviceProperty
        var deviceDesc = new STORAGE_DEVICE_DESCRIPTOR();
        
        // Step 1: Query with the base struct size just to retrieve the `Size` field.
        DeviceIoControl(
            handle,
            IOCTL_STORAGE_QUERY_PROPERTY,
            ref query,
            Marshal.SizeOf(query),
            ref deviceDesc,
            Marshal.SizeOf(deviceDesc),
            out _,
            IntPtr.Zero);

        if (deviceDesc.Size > 0)
        {
            // Step 2: Allocate the exact dynamically calculated size
            IntPtr buffer = Marshal.AllocHGlobal((int)deviceDesc.Size);
            try
            {
                success = DeviceIoControl(
                    handle,
                    IOCTL_STORAGE_QUERY_PROPERTY,
                    ref query,
                    Marshal.SizeOf(query),
                    buffer,
                    (int)deviceDesc.Size,
                    out _,
                    IntPtr.Zero);

                if (success)
                {
                    deviceDesc = Marshal.PtrToStructure<STORAGE_DEVICE_DESCRIPTOR>(buffer);
                    var busType = (StorageBusType)deviceDesc.BusType;
                    interfaceType = busType switch
                    {
                        StorageBusType.Sata => DriveInterfaceType.SATA,
                        StorageBusType.Usb => DriveInterfaceType.USB,
                        StorageBusType.Nvme => DriveInterfaceType.NVMe,
                        StorageBusType.Virtual or StorageBusType.FileBackedVirtual => DriveInterfaceType.Virtual,
                        _ => DriveInterfaceType.Unknown
                    };
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return new DriveClassification(mediaType, interfaceType);
    }

    private static bool IsNetworkDrive(string rootPath)
    {
        try
        {
            return Path.GetPathRoot(rootPath) is { Length: > 0 } root
                && new DriveInfo(root).DriveType == DriveType.Network;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
