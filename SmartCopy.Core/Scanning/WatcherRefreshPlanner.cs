using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Scanning;

public static class WatcherRefreshPlanner
{
    public static WatcherRefreshPlan CreatePlan(
        IFileSystemProvider provider,
        DirectoryTreeNode rootNode,
        IEnumerable<string> changedPaths)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(rootNode);
        ArgumentNullException.ThrowIfNull(changedPaths);

        var candidates = new List<WatcherRefreshTarget>();

        foreach (var changedPath in changedPaths)
        {
            if (string.IsNullOrWhiteSpace(changedPath))
            {
                continue;
            }

            string relativePath;
            try
            {
                relativePath = provider.GetRelativePath(rootNode.FullPath, changedPath);
            }
            catch (Exception)
            {
                return new WatcherRefreshPlan(provider, requiresFullRescan: true, refreshTargets: []);
            }

            var relativeSegments = provider.SplitPath(relativePath);
            if (IsOutsideRoot(relativeSegments))
            {
                continue;
            }

            candidates.Add(ResolveNearestExistingAncestor(rootNode, relativeSegments));
        }

        return new WatcherRefreshPlan(
            provider,
            requiresFullRescan: false,
            refreshTargets: RemoveRedundantDescendants(candidates));
    }

    private static WatcherRefreshTarget ResolveNearestExistingAncestor(
        DirectoryTreeNode rootNode,
        IReadOnlyList<string> relativeSegments)
    {
        if (relativeSegments.Count == 0)
        {
            return new WatcherRefreshTarget([]);
        }

        var current = rootNode;
        for (int i = 0; i < relativeSegments.Count; i++)
        {
            var segment = relativeSegments[i];
            var nextDirectory = current.Children.FirstOrDefault(child =>
                string.Equals(child.Name, segment, StringComparison.Ordinal));

            if (nextDirectory is not null)
            {
                current = nextDirectory;
                continue;
            }

            return new WatcherRefreshTarget(current.RelativePathSegments);
        }

        return new WatcherRefreshTarget(current.RelativePathSegments);
    }

    private static IReadOnlyList<WatcherRefreshTarget> RemoveRedundantDescendants(
        IEnumerable<WatcherRefreshTarget> candidates)
    {
        var ordered = candidates
            .Distinct(WatcherRefreshTargetComparer.Instance)
            .OrderBy(target => target.RelativePathSegments.Count)
            .ThenBy(target => target.CanonicalRelativePath, StringComparer.Ordinal)
            .ToList();

        var collapsed = new List<WatcherRefreshTarget>();
        foreach (var candidate in ordered)
        {
            if (collapsed.Any(existing => IsSameOrAncestor(existing.RelativePathSegments, candidate.RelativePathSegments)))
            {
                continue;
            }

            collapsed.Add(candidate);
        }

        return collapsed;
    }

    private static bool IsOutsideRoot(IReadOnlyList<string> relativeSegments)
    {
        return relativeSegments.Count > 0
               && string.Equals(relativeSegments[0], "..", StringComparison.Ordinal);
    }

    private static bool IsSameOrAncestor(
        IReadOnlyList<string> ancestorSegments,
        IReadOnlyList<string> candidateSegments)
    {
        if (ancestorSegments.Count > candidateSegments.Count)
        {
            return false;
        }

        for (int i = 0; i < ancestorSegments.Count; i++)
        {
            if (!string.Equals(ancestorSegments[i], candidateSegments[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private sealed class WatcherRefreshTargetComparer : IEqualityComparer<WatcherRefreshTarget>
    {
        public static WatcherRefreshTargetComparer Instance { get; } = new();

        public bool Equals(WatcherRefreshTarget? x, WatcherRefreshTarget? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return x.RelativePathSegments.Count == y.RelativePathSegments.Count
                   && IsSameOrAncestor(x.RelativePathSegments, y.RelativePathSegments);
        }

        public int GetHashCode(WatcherRefreshTarget obj)
        {
            var hash = new HashCode();
            foreach (var segment in obj.RelativePathSegments)
            {
                hash.Add(segment, StringComparer.Ordinal);
            }

            hash.Add(obj.RelativePathSegments.Count);
            return hash.ToHashCode();
        }
    }
}
