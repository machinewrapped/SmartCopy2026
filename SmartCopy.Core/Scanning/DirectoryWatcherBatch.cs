using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.Core.Scanning;

public sealed class DirectoryWatcherBatch
{
    public DirectoryWatcherBatch(
        bool requiresFullRescan,
        IReadOnlyList<DirectoryWatcherDeletion> deletions,
        IReadOnlyList<DirectoryWatcherUpsert> upserts)
    {
        RequiresFullRescan = requiresFullRescan;
        Deletions = deletions;
        Upserts = upserts;
    }

    public bool RequiresFullRescan { get; }
    public IReadOnlyList<DirectoryWatcherDeletion> Deletions { get; }
    public IReadOnlyList<DirectoryWatcherUpsert> Upserts { get; }
    public bool IsEmpty => !RequiresFullRescan && Deletions.Count == 0 && Upserts.Count == 0;
}

public sealed class DirectoryWatcherDeletion(IReadOnlyList<string> relativePathSegments)
{
    public IReadOnlyList<string> RelativePathSegments { get; } = [.. relativePathSegments];
    public string CanonicalRelativePath => string.Join("/", RelativePathSegments);
}

public sealed class DirectoryWatcherUpsert(IReadOnlyList<string> relativePathSegments, DirectoryTreeNode node)
{
    public IReadOnlyList<string> RelativePathSegments { get; } = [.. relativePathSegments];
    public DirectoryTreeNode Node { get; } = node;
    public string CanonicalRelativePath => string.Join("/", RelativePathSegments);
}
