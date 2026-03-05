using System.IO;

namespace SmartCopy.Tests;

public class TestAppDataStore : SmartCopy.Core.Settings.IAppDataStore
{
    public string BaseDirectory { get; }

    public TestAppDataStore()
    {
        BaseDirectory = Path.Combine(Path.GetTempPath(), "SmartCopy2026_Tests", System.Guid.NewGuid().ToString("N"));
    }

    public string GetFilePath(string fileName) => Path.Combine(BaseDirectory, fileName);
    public string GetDirectoryPath(string directoryName) => Path.Combine(BaseDirectory, directoryName);
}
