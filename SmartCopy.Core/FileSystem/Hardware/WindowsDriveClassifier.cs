using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace SmartCopy.Core.FileSystem.Hardware;

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
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
        public byte[] AdditionalParameters;
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

    public DriveClassification Classify(string rootPath)
    {
        var root = Path.GetPathRoot(rootPath);
        if (string.IsNullOrWhiteSpace(root))
            return DriveClassification.Unknown;

        string driveLetter = root.TrimEnd('\\');
        string devicePath = $@"\\.\{driveLetter}";

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
            AdditionalParameters = new byte[1]
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
        success = DeviceIoControl(
            handle,
            IOCTL_STORAGE_QUERY_PROPERTY,
            ref query,
            Marshal.SizeOf(query),
            ref deviceDesc,
            Marshal.SizeOf(deviceDesc),
            out _,
            IntPtr.Zero);

        if (success)
        {
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

        return new DriveClassification(mediaType, interfaceType);
    }
}
