using SmartCopy.Core.Settings;

namespace SmartCopy.Tests;

public class TestAppContext : IAppContext
{
    public AppSettings Settings { get; }
    public IAppDataStore DataStore { get; }

    public TestAppContext(AppSettings? settings = null)
    {
        Settings = settings ?? new AppSettings();
        DataStore = new TestAppDataStore();
    }
}
