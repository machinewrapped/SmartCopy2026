namespace SmartCopy.Core.DirectoryTree;

/// <summary>
/// The user's state in a directory tree — checked nodes, expanded folders and the active node —
/// keyed by relative path, so it survives a rescan that rebuilds every node.
/// </summary>
/// <remarks>
/// Paths are matched with the source provider's comparer, so a case-only rename on a
/// case-insensitive filesystem keeps its state. Files and folders are kept apart, so a file
/// replaced by a folder of the same name (or vice versa) does not inherit its check.
/// </remarks>
public sealed class DirectoryTreeState
{
    // A checked folder is recorded instead of its contents, so content added before the
    // rescan is checked too, as it would be if the folder had been checked after the scan.
    private readonly HashSet<string> _checkedFolders;
    private readonly HashSet<string> _checkedFiles;
    private readonly HashSet<string> _expandedFolders;
    private readonly StringComparer _pathComparer;
    private string? _selectedPath;
    private bool _selectedIsDirectory;

    private DirectoryTreeState(StringComparer pathComparer)
    {
        _pathComparer = pathComparer;
        _checkedFolders = new(pathComparer);
        _checkedFiles = new(pathComparer);
        _expandedFolders = new(pathComparer);
    }

    public static DirectoryTreeState Capture(DirectoryNode root, DirectoryTreeNode? selectedNode, StringComparer pathComparer)
    {
        var state = new DirectoryTreeState(pathComparer)
        {
            _selectedPath = selectedNode?.CanonicalRelativePath,
            _selectedIsDirectory = selectedNode?.IsDirectory ?? false,
        };
        state.CaptureFrom(root, parentChecked: false);
        return state;
    }

    /// <summary>
    /// Applies the state to a freshly scanned tree. Paths that no longer exist are skipped.
    /// </summary>
    /// <returns>The node that was active, or null if it no longer exists.</returns>
    public DirectoryTreeNode? Restore(DirectoryNode root)
    {
        DirectoryTreeNode? selected = null;
        RestoreFrom(root, ref selected);
        return selected;
    }

    private void CaptureFrom(DirectoryNode dir, bool parentChecked)
    {
        var isChecked = dir.CheckState == CheckState.Checked;
        if (isChecked && !parentChecked)
            _checkedFolders.Add(dir.CanonicalRelativePath);

        if (dir.IsExpanded)
            _expandedFolders.Add(dir.CanonicalRelativePath);

        if (dir.CheckState == CheckState.Indeterminate)
        {
            foreach (var file in dir.Files)
                if (file.CheckState == CheckState.Checked)
                    _checkedFiles.Add(file.CanonicalRelativePath);
        }

        foreach (var child in dir.Children)
            CaptureFrom(child, isChecked);
    }

    private void RestoreFrom(DirectoryNode dir, ref DirectoryTreeNode? selected)
    {
        var path = dir.CanonicalRelativePath;
        if (_selectedIsDirectory && IsSelectedPath(path))
            selected = dir;

        var dirChecked = _checkedFolders.Contains(path);
        if (dirChecked)
            dir.CheckState = CheckState.Checked;

        // Set files directly and recalculate once, rather than once per file.
        var anyChecked = false;
        foreach (var file in dir.Files)
        {
            var filePath = file.CanonicalRelativePath;
            if (!_selectedIsDirectory && IsSelectedPath(filePath))
                selected = file;

            if (!dirChecked && _checkedFiles.Contains(filePath))
            {
                file.SetCheckState(CheckState.Checked);
                anyChecked = true;
            }
        }

        if (anyChecked)
            dir.RecalculateCheckState();

        // After checking, which expands the folder
        dir.IsExpanded = _expandedFolders.Contains(path);

        foreach (var child in dir.Children)
            RestoreFrom(child, ref selected);
    }

    private bool IsSelectedPath(string path) =>
        _selectedPath is not null && _pathComparer.Equals(path, _selectedPath);
}
