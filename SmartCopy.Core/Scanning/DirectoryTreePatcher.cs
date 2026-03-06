using System.Collections.ObjectModel;
using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.Core.Scanning;

public static class DirectoryTreePatcher
{
    private static readonly StringComparer SortComparer = StringComparer.Ordinal;

    public static DirectoryTreePatchApplyResult Apply(
        DirectoryTreeNode rootNode,
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
            if (existingNode is not null)
            {
                continue;
            }

            var parentNode = FindNearestExistingParent(rootNode, insert.RelativePathSegments);
            if (parentNode is null || parentNode.IsMarkedForRemoval)
            {
                continue;
            }

            var insertedNode = MakeDirectoryNode(insert.Node, parentNode);
            InsertNodeIfMissing(parentNode, insertedNode);
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

    private static DirectoryTreeNode? FindExactNode(DirectoryTreeNode rootNode, IReadOnlyList<string> relativeSegments)
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

    private static DirectoryTreeNode? FindNearestExistingParent(DirectoryTreeNode rootNode, IReadOnlyList<string> relativeSegments)
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

    private static bool InsertNodeIfMissing(DirectoryTreeNode parentNode, DirectoryTreeNode node)
    {
        if (node.IsDirectory)
        {
            if (parentNode.Children.Any(child =>
                    !child.IsMarkedForRemoval && string.Equals(child.Name, node.Name, StringComparison.Ordinal)))
            {
                return false;
            }

            InsertAlphabetically(parentNode.Children, node);
            return true;
        }

        if (parentNode.Files.Any(file =>
                !file.IsMarkedForRemoval && string.Equals(file.Name, node.Name, StringComparison.Ordinal)))
        {
            return false;
        }

        InsertAlphabetically(parentNode.Files, node);
        return true;
    }

    private static void InsertAlphabetically(ObservableCollection<DirectoryTreeNode> collection, DirectoryTreeNode node)
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
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static DirectoryTreeNode MakeDirectoryNode(ScannedNode snapshotNode, DirectoryTreeNode parentNode)
    {
        var initialCheckState = parentNode.CheckState == CheckState.Checked
            ? CheckState.Checked
            : CheckState.Unchecked;

        var clone = new DirectoryTreeNode(snapshotNode.FileSystemNode, parentNode, initialCheckState);

        foreach (var child in snapshotNode.Children)
        {
            if (child.FileSystemNode.IsDirectory)
                clone.Children.Add(MakeDirectoryNode(child, clone));
            else
                clone.Files.Add(new DirectoryTreeNode(child.FileSystemNode, clone, initialCheckState));
        }

        return clone;
    }
}
