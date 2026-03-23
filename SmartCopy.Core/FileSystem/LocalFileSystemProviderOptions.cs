namespace SmartCopy.Core.FileSystem;

public enum LocalFileSystemWriteMode
{
    Auto,
    ManualLoop,
    CopyToAsync,
}

public sealed class LocalFileSystemProviderOptions
{
    public static LocalFileSystemProviderOptions Default { get; } = new();

    public int CopyBufferSizeBytes { get; init; } = 256 * 1024;
    public long SmallFileProgressThresholdBytes { get; init; } = 10L * 1024 * 1024;
    public LocalFileSystemWriteMode WriteMode { get; init; } = LocalFileSystemWriteMode.Auto;
    public bool UseArrayPoolForManualLoop { get; init; }
    public bool PreallocateDestinationFile { get; init; }

    public LocalFileSystemProviderOptions Normalize()
    {
        if (CopyBufferSizeBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(CopyBufferSizeBytes), "Copy buffer size must be positive.");
        }

        if (SmallFileProgressThresholdBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SmallFileProgressThresholdBytes),
                "Small-file progress threshold must be zero or greater.");
        }

        if (!Enum.IsDefined(WriteMode))
        {
            throw new ArgumentOutOfRangeException(nameof(WriteMode), "Write mode must be a defined value.");
        }

        return this;
    }
}
