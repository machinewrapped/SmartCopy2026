using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Scanning;

public sealed class WatcherRefreshPlan
{
    public WatcherRefreshPlan(
        IFileSystemProvider provider,
        bool requiresFullRescan,
        IReadOnlyList<WatcherRefreshTarget> refreshTargets)
    {
        Provider = provider ?? throw new ArgumentNullException(nameof(provider));
        RequiresFullRescan = requiresFullRescan;
        RefreshTargets = refreshTargets ?? throw new ArgumentNullException(nameof(refreshTargets));
    }

    public IFileSystemProvider Provider { get; }
    public bool RequiresFullRescan { get; }
    public IReadOnlyList<WatcherRefreshTarget> RefreshTargets { get; }
}

public sealed class WatcherRefreshTarget
{
    public WatcherRefreshTarget(IReadOnlyList<string> relativePathSegments)
    {
        RelativePathSegments = [.. relativePathSegments];
    }

    public IReadOnlyList<string> RelativePathSegments { get; }
    public string CanonicalRelativePath => string.Join("/", RelativePathSegments);
    public string ToFullPath(IFileSystemProvider provider, string rootPath) => provider.JoinPath(rootPath, RelativePathSegments);
}
