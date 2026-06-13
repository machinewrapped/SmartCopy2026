namespace SmartCopy.Core.FileSystem.Hardware;

public enum DriveMediaType
{
    Unknown,
    HDD,
    SSD,
    Memory,
    MTP
}

public enum DriveInterfaceType
{
    Unknown,
    SATA,
    USB,
    NVMe,
    Virtual
}

public readonly record struct DriveClassification(
    DriveMediaType MediaType,
    DriveInterfaceType InterfaceType)
{
    public static DriveClassification Unknown { get; } = 
        new(DriveMediaType.Unknown, DriveInterfaceType.Unknown);

    public override string ToString()
    {
        if (MediaType == DriveMediaType.Unknown && InterfaceType == DriveInterfaceType.Unknown)
            return "Unknown";
        if (MediaType == DriveMediaType.Unknown)
            return InterfaceType.ToString();
        if (InterfaceType == DriveInterfaceType.Unknown)
            return MediaType.ToString();
            
        return $"{MediaType} ({InterfaceType})";
    }
}
