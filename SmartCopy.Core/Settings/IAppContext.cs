namespace SmartCopy.Core.Settings;

public interface IAppContext
{
    AppSettings Settings { get; }
    IAppDataStore DataStore { get; }
}
