using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Settings;

public interface IAppContext : IPathResolver
{
    AppSettings Settings { get; }
    IAppDataStore DataStore { get; }
}
