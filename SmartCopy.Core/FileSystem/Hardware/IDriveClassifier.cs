namespace SmartCopy.Core.FileSystem.Hardware;

public interface IDriveClassifier
{
    DriveClassification Classify(string rootPath);
}
