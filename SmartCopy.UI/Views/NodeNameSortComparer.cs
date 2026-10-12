using System.Collections;
using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.UI.Views;

/// <summary>
/// DataGrid column comparer (it receives row items, not cell values) that orders nodes by name the
/// same way the scanner orders the tree, instead of the grid's default culture string sort.
/// </summary>
public sealed class NodeNameSortComparer : IComparer
{
    public static NodeNameSortComparer Instance { get; } = new();

    public int Compare(object? x, object? y) =>
        NaturalNameComparer.Instance.Compare((x as DirectoryTreeNode)?.Name, (y as DirectoryTreeNode)?.Name);
}
