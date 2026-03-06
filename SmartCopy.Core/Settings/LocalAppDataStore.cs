using System;
using System.IO;

namespace SmartCopy.Core.Settings;

public sealed class LocalAppDataStore : IAppDataStore
{
    public string BaseDirectory { get; }

    public LocalAppDataStore(string baseDirectory)
    {
        BaseDirectory = baseDirectory;
    }

    public string GetFilePath(string fileName)
    {
        return Path.Combine(BaseDirectory, fileName);
    }

    public string GetDirectoryPath(string directoryName)
    {
        return Path.Combine(BaseDirectory, directoryName);
    }

    public static LocalAppDataStore ForCurrentUser()
    {
        var baseDir = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartCopy2026")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "SmartCopy2026");

        return new LocalAppDataStore(baseDir);
    }

}
