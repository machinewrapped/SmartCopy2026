using System.Collections.ObjectModel;
using System.Diagnostics;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;

namespace SmartCopy.Core.DirectoryTree;

/// <summary>
/// Represents a directory node in the tree. Owns typed child collections and carries
/// all directory-specific logic: stats aggregation, traversal, and check-state propagation.
/// </summary>
public sealed class DirectoryNode : DirectoryTreeNode
{
    public DirectoryNode(
        FileSystemNode filesystemNode,
        DirectoryNode? parent,
        CheckState checkState = CheckState.Unchecked)
        : base(filesystemNode, parent, checkState)
    {
        Children.CollectionChanged += (_, _) => MarkDirty();
        Files.CollectionChanged    += (_, _) => MarkDirty();
    }

    public ObservableCollection<DirectoryNode> Children { get; } = [];
    public ObservableCollection<FileNode>      Files    { get; } = [];

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    // ── CheckState overrides ──────────────────────────────────────────────────

    protected override void OnChecked()  => IsExpanded = true;
    protected override void OnUnchecked() => IsExpanded = false;

    protected override void PropagateCheckStateDownward(CheckState newState)
    {
        if (newState == CheckState.Indeterminate || (Children.Count == 0 && Files.Count == 0))
            return;

        foreach (var child in Children)
        {
            if (child.CheckState != newState)
            {
                child.SetCheckState(newState);
                child.PropagateCheckStateDownward(newState);
            }
        }

        foreach (var file in Files)
        {
            if (file.CheckState != newState)
            {
                file.SetCheckState(newState);
            }
        }
    }

    internal void RecalculateCheckState()
    {
        if (Children.Count == 0 && Files.Count == 0) return;

        bool hasChecked = false;
        bool hasUnchecked = false;
        bool hasIndeterminate = false;

        foreach (var child in Children)
        {
            if (child.CheckState == CheckState.Checked) hasChecked = true;
            else if (child.CheckState == CheckState.Unchecked) hasUnchecked = true;
            else hasIndeterminate = true;
        }

        foreach (var file in Files)
        {
            if (file.CheckState == CheckState.Checked) hasChecked = true;
            else if (file.CheckState == CheckState.Unchecked) hasUnchecked = true;
            else hasIndeterminate = true;
        }

        CheckState computedState;
        if (hasIndeterminate || (hasChecked && hasUnchecked))
        {
            computedState = CheckState.Indeterminate;
        }
        else if (hasChecked)
        {
            computedState = CheckState.Checked;
        }
        else
        {
            computedState = CheckState.Unchecked;
        }

        if (CheckState != computedState)
        {
            SetCheckState(computedState);
            Parent?.RecalculateCheckState();
        }
    }

    // ── Stats ─────────────────────────────────────────────────────────────────

    public int NumSelectedFiles          { get; private set; }
    public long TotalSelectedBytes       { get; private set; }
    public int TotalFiles                { get; private set; }
    public int NumFilterIncludedFiles    { get; private set; }
    public long TotalFilterIncludedBytes { get; private set; }
    public int NumFilterExcludedFiles    => TotalFiles - NumFilterIncludedFiles;

    public void BuildStats()
    {
        int files = 0;
        long bytes = 0;
        int total = 0;
        int included = 0;
        long includedBytes = 0;

        foreach (var file in Files)
        {
            total++;
            if (file.IsFilterIncluded)
            {
                included++;
                includedBytes += file.Size;
            }
            if (file.IsSelected)
            {
                files++;
                bytes += file.Size;
            }
        }

        foreach (var child in Children)
        {
            if (child.IsDirty)
            {
                child.BuildStats();
            }

            files         += child.NumSelectedFiles;
            bytes         += child.TotalSelectedBytes;
            total         += child.TotalFiles;
            included      += child.NumFilterIncludedFiles;
            includedBytes += child.TotalFilterIncludedBytes;
        }

        foreach (var file in Files)
        {
            file.ClearDirty();
        }

        NumSelectedFiles         = files;
        TotalSelectedBytes       = bytes;
        TotalFiles               = total;
        NumFilterIncludedFiles   = included;
        TotalFilterIncludedBytes = includedBytes;
        ClearDirty();
    }

    // ── Removal ───────────────────────────────────────────────────────────────

    public override void MarkForRemoval()
    {
        if (IsMarkedForRemoval) return;

        IsMarkedForRemoval = true;

        foreach (var child in Children)
            child.MarkForRemoval();

        foreach (var file in Files)
            file.MarkForRemoval();
    }

    public void RemoveNodesMarkedForRemoval()
    {
        Debug.Assert(!IsMarkedForRemoval, "RemoveNodesMarkedForRemoval called on a node that's marked for removal - parent should have removed it.");

        // Iterate backwards to safely remove items while iterating
        for (var i = Files.Count - 1; i >= 0; i--)
        {
            if (Files[i].IsMarkedForRemoval)
                Files.RemoveAt(i);
        }

        // Remove any children marked for removal, and recurse into unmarked children
        bool removedAnyChildren = false;
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (Children[i].IsMarkedForRemoval)
            {
                Children.RemoveAt(i);
                removedAnyChildren = true;
            }
            else
            {
                Children[i].RemoveNodesMarkedForRemoval();
            }
        }

        if (removedAnyChildren)
        {
            MarkDirty();
            FilterChain.RecalculateParentExclusion(this);
        }
    }

    // ── Traversal ─────────────────────────────────────────────────────────────

    public IEnumerable<DirectoryTreeNode> GetSelectedDescendants()
    {
        foreach (var file in Files)
            if (file.IsSelected)
                yield return file;

        foreach (var child in Children)
        {
            if (child.IsSelected)
                yield return child;

            foreach (var desc in child.GetSelectedDescendants())
                yield return desc;
        }
    }

    /// <summary>
    /// Returns all non-excluded descendants (files and subdirectories) where
    /// <see cref="DirectoryTreeNode.FilterResult"/> != <see cref="FilterResult.Excluded"/>.
    /// Used by selection steps to avoid touching filter-excluded nodes' CheckState.
    /// </summary>
    public IEnumerable<DirectoryTreeNode> GetFilterIncludedDescendants()
    {
        foreach (var file in Files)
            if (file.IsFilterIncluded)
                yield return file;

        foreach (var child in Children)
        {
            if (child.IsFilterIncluded)
                yield return child;

            foreach (var desc in child.GetFilterIncludedDescendants())
                yield return desc;
        }
    }

    public DirectoryTreeNode? FindNodeByPathSegments(params string[] pathSegments)
    {
        var currentNode = this;
        for (int i = 0; i < pathSegments.Length; i++)
        {
            var segment = pathSegments[i];
            var nextNode = currentNode.Children.FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.Ordinal));

            if (nextNode is null)
            {
                // If we can't find a directory, check for a file, but only if it's the last segment.
                if (i == pathSegments.Length - 1)
                {
                    return currentNode.Files.FirstOrDefault(f => string.Equals(f.Name, segment, StringComparison.Ordinal));
                }
                // A directory segment in the middle of the path was not found.
                return null;
            }
            currentNode = nextNode;
        }
        return currentNode;
    }

    public override int CountSelectedFiles()   => Files.Count(f => f.IsSelected) + Children.Sum(c => c.CountSelectedFiles());
    public override int CountSelectedFolders() => (IsSelected ? 1 : 0) + Children.Sum(c => c.CountSelectedFolders());
    public override int CountAllFiles()        => Files.Count + Children.Sum(c => c.CountAllFiles());
    public override int CountAllFolders()      => 1 + Children.Sum(c => c.CountAllFolders());
}
