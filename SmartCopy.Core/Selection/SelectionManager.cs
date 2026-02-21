using System.Collections.Generic;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Selection;

public sealed class SelectionManager
{
    public SelectionSnapshot Capture(IEnumerable<FileSystemNode> roots)
    {
        var selected = new List<string>();
        foreach (var node in Traverse(roots))
        {
            if (node.IsSelected)
            {
                selected.Add(node.RelativePath);
            }
        }

        return new SelectionSnapshot(selected);
    }

    public void Restore(IEnumerable<FileSystemNode> roots, SelectionSnapshot snapshot)
    {
        foreach (var node in Traverse(roots))
        {
            node.CheckState = snapshot.Contains(node.RelativePath)
                ? CheckState.Checked
                : CheckState.Unchecked;
        }
    }

    private static IEnumerable<FileSystemNode> Traverse(IEnumerable<FileSystemNode> roots)
    {
        var stack = new Stack<FileSystemNode>(roots);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }
        }
    }
}

