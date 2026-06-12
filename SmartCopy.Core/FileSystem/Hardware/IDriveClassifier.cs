namespace SmartCopy.Core.FileSystem.Hardware;

public interface IDriveClassifier
{
    Task<DriveClassification> ClassifyAsync(string rootPath, CancellationToken ct = default);
}
