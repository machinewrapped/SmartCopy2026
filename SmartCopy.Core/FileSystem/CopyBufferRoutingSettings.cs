namespace SmartCopy.Core.FileSystem;

/// <summary>
/// Settings-backed copy-buffer routing profile used when
/// <see cref="OperationalSettings.DestinationRoutingEnabled"/> is true.
/// </summary>
public sealed record CopyBufferRoutingSettings
{
    public const int DefaultSsdBytes = 1024 * 1024;
    public const int DefaultUsbBytes = 1024 * 1024;
    public const int DefaultHddBytes = 512 * 1024;
    public const int DefaultSameVolumeHddBytes = 256 * 1024;
    public const int DefaultUnknownBytes = 512 * 1024;

    /// <summary>Routed copy buffer for SSD→SSD pairs.</summary>
    public int SsdBytes { get; init; } = DefaultSsdBytes;
    /// <summary>Routed copy buffer for USB destinations, unless same-volume HDD rules apply.</summary>
    public int UsbBytes { get; init; } = DefaultUsbBytes;
    /// <summary>Routed copy buffer for cross-volume pairs where either side is HDD.</summary>
    public int HddBytes { get; init; } = DefaultHddBytes;
    /// <summary>Routed copy buffer for same-volume HDD copies.</summary>
    public int SameVolumeHddBytes { get; init; } = DefaultSameVolumeHddBytes;
    /// <summary>Routed copy buffer for unknown, memory, MTP, or otherwise ambiguous media pairs.</summary>
    public int UnknownBytes { get; init; } = DefaultUnknownBytes;

    public CopyBufferRoutingSettings Normalize()
    {
        ValidatePositiveBuffer(SsdBytes, nameof(SsdBytes));
        ValidatePositiveBuffer(UsbBytes, nameof(UsbBytes));
        ValidatePositiveBuffer(HddBytes, nameof(HddBytes));
        ValidatePositiveBuffer(SameVolumeHddBytes, nameof(SameVolumeHddBytes));
        ValidatePositiveBuffer(UnknownBytes, nameof(UnknownBytes));

        return this;
    }

    private static void ValidatePositiveBuffer(int value, string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Copy buffer size must be positive.");
        }
    }
}
