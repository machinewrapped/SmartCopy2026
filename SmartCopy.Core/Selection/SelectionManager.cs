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
        var matchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in Traverse(root))
        {
            // Accept snapshots saved with either relative or absolute paths.
            // Track matched keys using whichever form the snapshot contains so that
            // the unmatched calculation below works correctly for both cases.
            if (snapshot.Contains(node.CanonicalRelativePath))
            {
                node.CheckState = CheckState.Checked;
                matchedKeys.Add(node.CanonicalRelativePath);
            }
            else if (snapshot.Contains(node.FullPath))
            {
                node.CheckState = CheckState.Checked;
                matchedKeys.Add(node.FullPath);
            }
            else
            {
                node.CheckState = CheckState.Unchecked;
            }
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
