using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.Core.Selection;

public sealed class SelectionManager
{
    public SelectionSnapshot Capture(DirectoryNode root, bool useAbsolutePaths = false)
    {
        var selected = new List<string>();
        foreach (var node in Traverse(root))
        {
            if (node.IsSelected)
                selected.Add(useAbsolutePaths ? node.FullPath : node.CanonicalRelativePath);
        }

        return new SelectionSnapshot(selected);
    }

    public SelectionRestoreResult Restore(DirectoryNode root, SelectionSnapshot snapshot)
    {
        var matchedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in Traverse(root))
        {
            // Accept snapshots saved with either relative or absolute paths.
            // Track matched keys using whichever form the snapshot contains so that
            // the unmatched calculation below works correctly for both cases.
            var matchedPath = snapshot.Contains(node.CanonicalRelativePath) ? node.CanonicalRelativePath
                            : snapshot.Contains(node.FullPath)              ? node.FullPath
                            : null;

            node.CheckState = matchedPath is not null ? CheckState.Checked : CheckState.Unchecked;
            if (matchedPath is not null)
                matchedKeys.Add(matchedPath);
        }

        var unmatched = snapshot.Paths.Where(p => !matchedKeys.Contains(p)).ToList();
        return new SelectionRestoreResult(matchedKeys.Count, unmatched);
    }

    public void SelectAll(DirectoryNode root)
    {
        foreach (var n in Traverse(root))
            if (n is FileNode fileNode)
                fileNode.CheckState = CheckState.Checked;
    }

    public void ClearAll(DirectoryNode root)
    {
        foreach (var n in Traverse(root))
            if (n is FileNode fileNode)
                fileNode.CheckState = CheckState.Unchecked;
    }

    public void InvertAll(DirectoryNode root)
    {
        // Only invert file nodes — directories derive their state from children.
        foreach (var n in Traverse(root))
            if (n is FileNode fileNode)
                fileNode.CheckState = fileNode.CheckState == CheckState.Checked
                    ? CheckState.Unchecked
                    : CheckState.Checked;
    }

    public SelectionRestoreResult RemoveFromSnapshot(DirectoryNode root, SelectionSnapshot snapshot)
    {
        var matchedKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in Traverse(root))
        {
            var matchedPath = snapshot.Contains(node.CanonicalRelativePath) ? node.CanonicalRelativePath
                            : snapshot.Contains(node.FullPath)              ? node.FullPath
                            : null;

            if (matchedPath is not null)
            {
                node.CheckState = CheckState.Unchecked;
                matchedKeys.Add(matchedPath);
            }
            // else: leave current CheckState untouched
        }

        var unmatched = snapshot.Paths.Where(p => !matchedKeys.Contains(p)).ToList();
        return new SelectionRestoreResult(matchedKeys.Count, unmatched);
    }

    public void ExpandSelectedFolders(DirectoryNode root)
    {
        foreach (var node in Traverse(root))
        {
            if (node is DirectoryNode dir && dir.CheckState != CheckState.Unchecked)
                dir.IsExpanded = true;
        }
    }

    public void SelectAllFilesInSelectedFolders(DirectoryNode root)
    {
        foreach (var node in Traverse(root))
        {
            if (node is DirectoryNode dir && dir.CheckState != CheckState.Unchecked)
            {
                foreach (var file in dir.Files)
                {
                    if (file.IsFilterIncluded && file.CheckState == CheckState.Unchecked)
                        file.CheckState = CheckState.Checked;
                }
            }
        }
    }

    private static IEnumerable<DirectoryTreeNode> Traverse(DirectoryNode root)
    {
        var stack = new Stack<DirectoryNode>([root]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var file in node.Files)
                yield return file;

            for (var i = node.Children.Count - 1; i >= 0; i--)
                stack.Push(node.Children[i]);
        }
    }
}
