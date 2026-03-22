namespace SmartCopy.Core.FileSystem;

public sealed class LocalFileSystemProviderOptions
{
    public static LocalFileSystemProviderOptions Default { get; } = new();

    public int CopyBufferSizeBytes { get; init; } = 256 * 1024;
    public long SmallFileProgressThresholdBytes { get; init; } = 10L * 1024 * 1024;

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

        return this;
    }
}
