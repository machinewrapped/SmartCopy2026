using System.Collections.ObjectModel;
using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.Core.Scanning;

public static class DirectoryTreePatcher
{
    // Must match the order DirectoryScanner gives siblings, or inserts land out of place
    private static readonly IComparer<string> SortComparer = NaturalNameComparer.Instance;

    public static DirectoryTreePatchApplyResult Apply(
        DirectoryNode rootNode,
        DirectoryWatcherBatch batch,
        DirectoryTreeNode? selectedNode)
    {
        ArgumentNullException.ThrowIfNull(rootNode);
        ArgumentNullException.ThrowIfNull(batch);

        DirectoryTreeNode? nextSelectedNode = selectedNode;

        foreach (var deletion in batch.Deletions)
        {
            var node = FindExactNode(rootNode, deletion.RelativePathSegments);
            if (node is null)
            {
                continue;
            }

            node.MarkForRemoval();
            if (nextSelectedNode is not null && IsDescendantOrSelf(nextSelectedNode, node))
            {
                nextSelectedNode = node.Parent ?? rootNode;
            }
        }

        foreach (var insert in batch.Inserts.OrderBy(u => u.RelativePathSegments.Count))
        {
            var existingNode = FindExactNode(rootNode, insert.RelativePathSegments);

            // Deleted and recreated within the batch: the insert replaces the removed node rather
            // than being skipped, which would leave the path missing from the tree. A replacement
            // of the same kind keeps the node's check, expansion and selection; a file replaced by
            // a folder (or the reverse) starts afresh.
            DirectoryTreeNode? replaced = null;
            if (existingNode is { IsMarkedForRemoval: true, Parent: { } existingParent })
            {
                if (existingNode is DirectoryNode existingDir)
                    existingParent.Children.Remove(existingDir);
                else if (existingNode is FileNode existingFile)
                    existingParent.Files.Remove(existingFile);

                if (existingNode.IsDirectory == insert.Node.FileSystemNode.IsDirectory)
                    replaced = existingNode;
            }
            else if (existingNode is not null)
            {
                continue;
            }

            var parentNode = FindNearestExistingParent(rootNode, insert.RelativePathSegments);
            if (parentNode is null || parentNode.IsMarkedForRemoval)
            {
                continue;
            }

            if (insert.Node.FileSystemNode.IsDirectory)
            {
                var insertedDirNode = MakeDirectoryNode(insert.Node, parentNode,
                    replaced?.CheckState == CheckState.Checked ? CheckState.Checked : null);
                if (replaced is DirectoryNode { IsExpanded: true })
                    insertedDirNode.IsExpanded = true;
                InsertDirAlphabetically(parentNode.Children, insertedDirNode);
            }
            else
            {
                var existingFile = parentNode.Files.FirstOrDefault(f =>
                    !f.IsMarkedForRemoval && string.Equals(f.Name, insert.Node.FileSystemNode.Name, StringComparison.Ordinal));
                if (existingFile is null)
                {
                    var initialCheckState = replaced?.CheckState
                        ?? (parentNode.CheckState == CheckState.Checked ? CheckState.Checked : CheckState.Unchecked);
                    var fileNode = new FileNode(insert.Node.FileSystemNode, parentNode, initialCheckState);
                    InsertFileAlphabetically(parentNode.Files, fileNode);
                }
            }

            // The deletion moved the selection off the replaced node; move it to its replacement
            if (replaced is not null && selectedNode is not null && IsDescendantOrSelf(selectedNode, replaced))
            {
                nextSelectedNode = FindExactNode(rootNode, selectedNode.RelativePathSegments) ?? nextSelectedNode;
            }
        }

        foreach (var refresh in batch.Refreshes)
        {
            var node = FindExactNode(rootNode, refresh.RelativePathSegments);
            if (node is null)
            {
                continue;
            }

            node.UpdateFrom(refresh.Node);
        }

        rootNode.BuildStats();
        return new DirectoryTreePatchApplyResult(nextSelectedNode);
    }

    private static DirectoryTreeNode? FindExactNode(DirectoryNode rootNode, IReadOnlyList<string> relativeSegments)
    {
        if (relativeSegments.Count == 0)
        {
            return rootNode;
        }

        var currentNode = rootNode;
        for (int i = 0; i < relativeSegments.Count; i++)
        {
            var segment = relativeSegments[i];
            var nextDirectory = currentNode.Children.FirstOrDefault(child =>
                string.Equals(child.Name, segment, StringComparison.Ordinal));

            if (nextDirectory is not null)
            {
                currentNode = nextDirectory;
                continue;
            }

            if (i == relativeSegments.Count - 1)
            {
                return currentNode.Files.FirstOrDefault(file =>
                    string.Equals(file.Name, segment, StringComparison.Ordinal));
            }

            return null;
        }

        return currentNode;
    }

    private static DirectoryNode? FindNearestExistingParent(DirectoryNode rootNode, IReadOnlyList<string> relativeSegments)
    {
        if (relativeSegments.Count == 0)
        {
            return rootNode.Parent;
        }

        var currentNode = rootNode;
        for (int i = 0; i < relativeSegments.Count - 1; i++)
        {
            var segment = relativeSegments[i];
            var nextDirectory = currentNode.Children.FirstOrDefault(child =>
                string.Equals(child.Name, segment, StringComparison.Ordinal));

            if (nextDirectory is null)
            {
                return currentNode;
            }

            currentNode = nextDirectory;
        }

        return currentNode;
    }

    private static void InsertDirAlphabetically(ObservableCollection<DirectoryNode> collection, DirectoryNode node)
    {
        var insertIndex = 0;
        while (insertIndex < collection.Count
               && SortComparer.Compare(collection[insertIndex].Name, node.Name) <= 0)
        {
            insertIndex++;
        }

        collection.Insert(insertIndex, node);
    }

    private static void InsertFileAlphabetically(ObservableCollection<FileNode> collection, FileNode node)
    {
        var insertIndex = 0;
        while (insertIndex < collection.Count
               && SortComparer.Compare(collection[insertIndex].Name, node.Name) <= 0)
        {
            insertIndex++;
        }

        collection.Insert(insertIndex, node);
    }

    private static bool IsDescendantOrSelf(DirectoryTreeNode node, DirectoryTreeNode ancestor)
    {
        for (DirectoryTreeNode? current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    private static DirectoryNode MakeDirectoryNode(
        ScannedNode snapshotNode, DirectoryNode parentNode, CheckState? checkState = null)
    {
        var initialCheckState = checkState
            ?? (parentNode.CheckState == CheckState.Checked ? CheckState.Checked : CheckState.Unchecked);

        var clone = new DirectoryNode(snapshotNode.FileSystemNode, parentNode, initialCheckState);

        foreach (var child in snapshotNode.Children)
        {
            if (child.FileSystemNode.IsDirectory)
                clone.Children.Add(MakeDirectoryNode(child, clone));
            else
                clone.Files.Add(new FileNode(child.FileSystemNode, clone, initialCheckState));
        }

        return clone;
    }
}
