namespace SmartCopy.Core.DirectoryTree;

/// <summary>
/// The user's state in a directory tree — checked nodes, expanded folders and the active node —
/// keyed by relative path, so it survives a rescan that rebuilds every node.
/// </summary>
public sealed class DirectoryTreeState
{
    // A checked folder is recorded instead of its contents, so content added before the
    // rescan is checked too, as it would be if the folder had been checked after the scan.
    private readonly HashSet<string> _checkedPaths = new(StringComparer.Ordinal);
    private readonly HashSet<string> _expandedPaths = new(StringComparer.Ordinal);
    private string[]? _selectedPathSegments;

    public static DirectoryTreeState Capture(DirectoryNode root, DirectoryTreeNode? selectedNode)
    {
        var state = new DirectoryTreeState { _selectedPathSegments = selectedNode?.RelativePathSegments };
        state.CaptureFrom(root, parentChecked: false);
        return state;
    }

    /// <summary>
    /// Applies the state to a freshly scanned tree. Paths that no longer exist are skipped.
    /// </summary>
    /// <returns>The node that was active, or null if it no longer exists.</returns>
    public DirectoryTreeNode? Restore(DirectoryNode root)
    {
        RestoreFrom(root);
        return _selectedPathSegments is null ? null : root.FindNodeByPathSegments(_selectedPathSegments);
    }

    private void CaptureFrom(DirectoryNode dir, bool parentChecked)
    {
        var isChecked = dir.CheckState == CheckState.Checked;
        if (isChecked && !parentChecked)
            _checkedPaths.Add(dir.CanonicalRelativePath);

        if (dir.IsExpanded)
            _expandedPaths.Add(dir.CanonicalRelativePath);

        if (dir.CheckState == CheckState.Indeterminate)
        {
            foreach (var file in dir.Files)
                if (file.CheckState == CheckState.Checked)
                    _checkedPaths.Add(file.CanonicalRelativePath);
        }

        foreach (var child in dir.Children)
            CaptureFrom(child, isChecked);
    }

    private void RestoreFrom(DirectoryNode dir)
    {
        if (_checkedPaths.Contains(dir.CanonicalRelativePath))
        {
            dir.CheckState = CheckState.Checked;
        }
        else
        {
            // Set files directly and recalculate once, rather than once per file.
            var anyChecked = false;
            foreach (var file in dir.Files)
            {
                if (_checkedPaths.Contains(file.CanonicalRelativePath))
                {
                    file.SetCheckState(CheckState.Checked);
                    anyChecked = true;
                }
            }

            if (anyChecked)
                dir.RecalculateCheckState();
        }

        // After checking, which expands the folder
        dir.IsExpanded = _expandedPaths.Contains(dir.CanonicalRelativePath);

        foreach (var child in dir.Children)
            RestoreFrom(child);
    }
}
