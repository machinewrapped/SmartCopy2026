using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.Core.Selection;

public sealed class SelectionManager
{
    public SelectionSnapshot Capture(IEnumerable<DirectoryTreeNode> roots, bool useAbsolutePaths = false)
    {
        var selected = new List<string>();
        foreach (var node in Traverse(roots))
        {
            if (node.IsSelected)
                selected.Add(useAbsolutePaths ? node.FullPath : node.CanonicalRelativePath);
        }

        return new SelectionSnapshot(selected);
    }

    public SelectionRestoreResult Restore(IEnumerable<DirectoryTreeNode> roots, SelectionSnapshot snapshot)
    {
        var matchedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in Traverse(roots))
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

    public void SelectAll(IEnumerable<DirectoryTreeNode> roots)
    {
        foreach (var n in Traverse(roots))
            n.CheckState = CheckState.Checked;
    }

    public void ClearAll(IEnumerable<DirectoryTreeNode> roots)
    {
        foreach (var n in Traverse(roots))
            n.CheckState = CheckState.Unchecked;
    }

    public void InvertAll(IEnumerable<DirectoryTreeNode> roots)
    {
        // Only invert leaf nodes (no children, no files of their own) to avoid
        // cascade-down overwriting individual file states when a parent is processed.
        foreach (var n in Traverse(roots))
            if (n.Children.Count == 0 && n.Files.Count == 0)
                n.CheckState = n.CheckState == CheckState.Checked ? CheckState.Unchecked : CheckState.Checked;
    }

    private static IEnumerable<DirectoryTreeNode> Traverse(IEnumerable<DirectoryTreeNode> roots)
    {
        var stack = new Stack<DirectoryTreeNode>(roots);
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
