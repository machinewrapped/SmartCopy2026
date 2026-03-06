using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Scanning;

public sealed class LocalDirectoryWatcherFactory : IDirectoryWatcherFactory
{
    public IDirectoryWatcher Create(IFileSystemProvider provider, string path)
    {
        if (provider is not LocalFileSystemProvider)
        {
            throw new InvalidOperationException("Directory watchers are only supported for local filesystem providers.");
        }

        return new DirectoryWatcher(path);
    }
}
