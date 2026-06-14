namespace SmartCopy.Core.FileSystem.Hardware;

public static class CrossPlatformDriveClassifier
{
    private static readonly IDriveClassifier _classifier = CreateClassifier();

    private static IDriveClassifier CreateClassifier()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsDriveClassifier();
        if (OperatingSystem.IsLinux())
            return new LinuxDriveClassifier();
        if (OperatingSystem.IsMacOS())
            return new MacDriveClassifier();
        
        return new FallbackDriveClassifier();
    }

    public static Task<DriveClassification> ClassifyAsync(string rootPath, CancellationToken ct = default)
    {
        return _classifier.ClassifyAsync(rootPath, ct);
    }

    private sealed class FallbackDriveClassifier : IDriveClassifier
    {
        public Task<DriveClassification> ClassifyAsync(string rootPath, CancellationToken ct = default) => Task.FromResult(DriveClassification.Unknown);
    }
}
