using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Scanning;

public sealed class DirectoryWatcherBatch
{
    public DirectoryWatcherBatch(
        bool requiresFullRescan,
        DirectoryWatcherDeletion[] deletions,
        DirectoryWatcherInsert[] inserts,
        DirectoryWatcherRefresh[] refreshes)
    {
        RequiresFullRescan = requiresFullRescan;
        Deletions = deletions;
        Inserts = inserts;
        Refreshes = refreshes;
    }

    public bool RequiresFullRescan { get; }
    public DirectoryWatcherDeletion[] Deletions { get; }
    public DirectoryWatcherInsert[] Inserts { get; }
    public DirectoryWatcherRefresh[] Refreshes { get; }
    public bool IsEmpty => !RequiresFullRescan && Deletions.Length == 0 && Inserts.Length == 0 && Refreshes.Length == 0;
}

public sealed class DirectoryWatcherDeletion(IReadOnlyList<string> relativePathSegments)
{
    public IReadOnlyList<string> RelativePathSegments { get; } = [.. relativePathSegments];
    public string CanonicalRelativePath => string.Join("/", RelativePathSegments);
    public override string ToString() => $"Deletion({CanonicalRelativePath})";
}

public sealed class DirectoryWatcherInsert(IReadOnlyList<string> relativePathSegments, DirectoryTreeNode node)
{
    public IReadOnlyList<string> RelativePathSegments { get; } = [.. relativePathSegments];
    public DirectoryTreeNode Node { get; } = node;
    public string CanonicalRelativePath => string.Join("/", RelativePathSegments);
    public override string ToString() => $"Insert({CanonicalRelativePath})";
}

public sealed class DirectoryWatcherRefresh(IReadOnlyList<string> relativePathSegments, FileSystemNode node)
{
    public IReadOnlyList<string> RelativePathSegments { get; } = [.. relativePathSegments];
    public FileSystemNode Node { get; } = node;
    public string CanonicalRelativePath => string.Join("/", RelativePathSegments);
    public override string ToString() => $"Refresh({CanonicalRelativePath})";
}