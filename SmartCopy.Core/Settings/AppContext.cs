namespace SmartCopy.Core.Settings;

public sealed class SmartCopyAppContext : IAppContext
{
    public AppSettings Settings { get; }
    public IAppDataStore DataStore { get; }

    public SmartCopyAppContext(AppSettings settings, IAppDataStore dataStore)
    {
        Settings = settings;
        DataStore = dataStore;
    }
}
