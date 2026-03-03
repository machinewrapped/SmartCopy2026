using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;

namespace SmartCopy.Core.DirectoryTree;

public sealed class DirectoryTreeNode(
    FileSystemNode _filesystemNode,
    DirectoryTreeNode? _parent,
    CheckState _checkState = CheckState.Unchecked) : INotifyPropertyChanged
{
    public string Name => _filesystemNode.Name;
    public string FullPath => _filesystemNode.FullPath;
    public bool IsDirectory => _filesystemNode.IsDirectory;
    public long Size => _filesystemNode.Size;
    public DateTime CreatedAt => _filesystemNode.CreatedAt;
    public DateTime ModifiedAt => _filesystemNode.ModifiedAt;
    public FileAttributes Attributes => _filesystemNode.Attributes;

    public string[] RelativePathSegments {get; init; } = _parent is null ? Array.Empty<string>() : [.. _parent.RelativePathSegments.Append(_filesystemNode.Name)];
    public string CanonicalRelativePath => string.Join("/", RelativePathSegments);

    public override string ToString() => CanonicalRelativePath + (IsDirectory ? "/" : "");

    public bool IsDirty { get; private set; } = true;
    public int NumSelectedFiles { get; private set; }
    public long TotalSelectedBytes { get; private set; }

    public CheckState CheckState
    {
        get => _checkState;
        set
        {
            if (_checkState != value)
            {
                SetCheckStateWithPropagation(value);

                if (value == CheckState.Checked)
                {
                    IsExpanded = true;
                }
                else if (value == CheckState.Unchecked && Parent is not null)
                {
                    IsExpanded = false;
                }
            }
        }
    }

    private FilterResult _filterResult = FilterResult.Included;
    public FilterResult FilterResult
    {
        get => _filterResult;
        set
        {
            if (_filterResult != value)
            {
                _filterResult = value;
                MarkDirty();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSelected));
                OnPropertyChanged(nameof(IsFilterIncluded));
                OnPropertyChanged(nameof(IsAtomicIncluded));
            }
        }
    }

    private bool _isMarkedForRemoval;
    public bool IsMarkedForRemoval
    {
        get => _isMarkedForRemoval;
        set
        {
            if (_isMarkedForRemoval != value)
            {
                _isMarkedForRemoval = value;
                OnPropertyChanged();
            }
        }
    }

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

    public bool IsSelected => CheckState == CheckState.Checked && FilterResult == FilterResult.Included;
    public bool IsFilterIncluded => FilterResult != FilterResult.Excluded;
    public bool IsAtomicIncluded => FilterResult == FilterResult.Included;

    public DirectoryTreeNode? Parent { get; init; } = _parent;
    public ObservableCollection<DirectoryTreeNode> Children { get; } = [];
    public ObservableCollection<DirectoryTreeNode> Files { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    public void MarkDirty()
    {
        if (IsDirty) return;

        IsDirty = true;
        OnPropertyChanged(nameof(IsDirty));
        Parent?.MarkDirty();
    }

    public void ClearDirty() => IsDirty = false;

    public void BuildStats()
    {
        int files = 0;
        long bytes = 0;
        foreach (var file in Files)
        {
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

            files += child.NumSelectedFiles;
            bytes += child.TotalSelectedBytes;
        }

        NumSelectedFiles = files;
        TotalSelectedBytes = bytes;
        ClearDirty();
    }

    public void MarkForRemoval()
    {
        if (IsMarkedForRemoval) return;

        IsMarkedForRemoval = true;

        foreach (var child in Children)
            child.MarkForRemoval();

        foreach (var file in Files)
            file.MarkForRemoval();
    }

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
    /// <see cref="FilterResult"/> != <see cref="FilterResult.Excluded"/>.
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

    public DirectoryTreeNode? FindNodeByPathSegments(string[] pathSegments)
    {
        var currentNode = this;
        for (int i = 0; i < pathSegments.Length; i++)
        {
            var segment = pathSegments[i];
            var nextNode = currentNode.Children.FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.OrdinalIgnoreCase));

            if (nextNode is null)
            {
                // If we can't find a directory, check for a file, but only if it's the last segment.
                if (i == pathSegments.Length - 1)
                {
                    return currentNode.Files.FirstOrDefault(f => string.Equals(f.Name, segment, StringComparison.OrdinalIgnoreCase));
                }
                // A directory segment in the middle of the path was not found.
                return null;
            }
            currentNode = nextNode;
        }
        return currentNode;
    }
    
    public void RemoveNodesMarkedForRemoval()
    {
        Debug.Assert(!IsMarkedForRemoval, "RemoveNodesMarkedForRemoval called on a node that's marked for removal - parent should have removed it.");

        // Iterate backwards to safely remove items while iterating
        for (var i = Files.Count - 1; i >= 0; i--)
        {
            var file = Files[i];
            if (file.IsMarkedForRemoval)
            {
                Files.RemoveAt(i);
            }
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
            // If we removed any children, we may need to recalculate our filter state
            FilterChain.RecalculateParentExclusion(this);
        }
    }


    internal int CountSelectedFiles() =>
        (IsSelected && !IsDirectory ? 1 : 0) + (IsDirectory ? Children.Sum(c => c.CountSelectedFiles()) + Files.Count(f => f.IsSelected) : 0);

    internal int CountSelectedFolders() =>
        (IsSelected && IsDirectory ? 1 : 0) + (IsDirectory ? Children.Sum(c => c.CountSelectedFolders()) : 0);

    internal int CountAllFiles() =>
        IsDirectory ? Files.Count + Children.Sum(c => c.CountAllFiles()) : 1;

    internal int CountAllFolders() =>
        IsDirectory ? 1 + Children.Sum(c => c.CountAllFolders()) : 0;

    void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void SetCheckStateWithPropagation(CheckState newState)
    {
        // Change self
        _checkState = newState;

        MarkDirty();

        // Downward propagation
        if (newState != CheckState.Indeterminate && (Children.Count > 0 || Files.Count > 0))
        {
            PropagateDownward(newState);
        }

        OnPropertyChanged(nameof(CheckState));
        OnPropertyChanged(nameof(IsSelected));

        // Upward recalculation
        Parent?.RecalculateCheckState();
    }

    private void PropagateDownward(CheckState newState)
    {
        foreach (var child in Children)
        {
            if (child.CheckState != newState)
            {
                child.SetCheckState(newState);
                if (child.Children.Count > 0 || child.Files.Count > 0)
                {
                    child.PropagateDownward(newState);
                }
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

    private void SetCheckState(CheckState newState)
    {
        _checkState = newState;
        MarkDirty();
        OnPropertyChanged(nameof(CheckState));
        OnPropertyChanged(nameof(IsSelected));
    }

    private void RecalculateCheckState()
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
}
