using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Scanning;

public interface IDirectoryWatcherFactory
{
    IDirectoryWatcher Create(IFileSystemProvider provider, string path);
}
