namespace SmartCopy.Core.Settings;

public interface IAppDataStore
{
    string BaseDirectory { get; }
    string GetFilePath(string fileName);
    string GetDirectoryPath(string directoryName);
}
