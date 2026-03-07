using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.Core.Scanning;

public sealed record DirectoryTreePatchApplyResult(
    DirectoryTreeNode? SelectedNode);
