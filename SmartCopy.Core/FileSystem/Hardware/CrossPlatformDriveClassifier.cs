using System.Runtime.InteropServices;

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

    public static DriveClassification Classify(string rootPath)
    {
        return _classifier.Classify(rootPath);
    }

    private sealed class FallbackDriveClassifier : IDriveClassifier
    {
        public DriveClassification Classify(string rootPath) => DriveClassification.Unknown;
    }
}
