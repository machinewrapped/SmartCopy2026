using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.DirectoryTree;

/// <summary>
/// Represents a file node in the directory tree. Lightweight — carries no child collections.
/// Files always have a <see cref="DirectoryNode"/> parent (or null for a file-as-root edge case).
/// </summary>
public sealed class FileNode : DirectoryTreeNode
{
    public FileNode(
        FileSystemNode filesystemNode,
        DirectoryNode? parent,
        CheckState checkState = CheckState.Unchecked)
        : base(filesystemNode, parent, checkState)
    {
    }

    public override void MarkForRemoval()
    {
        if (IsMarkedForRemoval) return;

        IsMarkedForRemoval = true;
    }
}
