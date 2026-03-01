using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.DirectoryTree;

public sealed class DirectoryTreeNode(FileSystemNode _filesystemNode, DirectoryTreeNode? _parent, CheckState _checkState = CheckState.Unchecked) : INotifyPropertyChanged
{
    public string Name => _filesystemNode.Name;
    public string FullPath => _filesystemNode.FullPath;
    public bool IsDirectory => _filesystemNode.IsDirectory;
    public long Size => _filesystemNode.Size;
    public DateTime CreatedAt => _filesystemNode.CreatedAt;
    public DateTime ModifiedAt => _filesystemNode.ModifiedAt;
    public FileAttributes Attributes => _filesystemNode.Attributes;

    public string[] RelativePathSegments {get; init; } = _parent is null ? Array.Empty<string>() : _parent.RelativePathSegments.Append(_filesystemNode.Name).ToArray();
    public string CanonicalRelativePath => string.Join("/", RelativePathSegments);

    public override string ToString() => CanonicalRelativePath + (IsDirectory ? "/" : "");

    private int _batchUpdateDepth = 0;

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
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSelected));
                OnPropertyChanged(nameof(IsFilterIncluded));
                OnPropertyChanged(nameof(IsAtomicIncluded));
            }
        }
    }

    private string? _excludedByFilter;
    public string? ExcludedByFilter
    {
        get => _excludedByFilter;
        set
        {
            if (_excludedByFilter != value)
            {
                _excludedByFilter = value;
                OnPropertyChanged();
            }
        }
    }

    private string? _notes;
    public string? Notes
    {
        get => _notes;
        set
        {
            if (_notes != value)
            {
                _notes = value;
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

    public IDisposable BeginBatchUpdate()
    {
        _batchUpdateDepth++;
        return new BatchUpdateScope(this);
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

    public DirectoryTreeNode? FindNodeByPathSegments(string[] pathSegments)
    {
        if (pathSegments.Length == 0)
            return this;

        var segment = pathSegments[0];
        var nextNode = Children.FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.OrdinalIgnoreCase));
        if (nextNode is null)
        {
            var fileNode = Files.FirstOrDefault(f => string.Equals(f.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (fileNode != null)
            {
                Debug.Assert(pathSegments.Length == 1, "Path segments after matching file node.");
                return fileNode;
            }

            return null;
        }

        return nextNode.FindNodeByPathSegments(pathSegments.Skip(1).ToArray());
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
        if (_batchUpdateDepth == 0)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    private void EndBatchUpdate()
    {
        _batchUpdateDepth--;
        if (_batchUpdateDepth == 0)
        {
            OnPropertyChanged(); // Reset bindings
        }
    }

    private void SetCheckStateWithPropagation(CheckState newState)
    {
        // Change self
        _checkState = newState;
        OnPropertyChanged(nameof(CheckState));
        OnPropertyChanged(nameof(IsSelected));

        // Downward propagation
        if (newState != CheckState.Indeterminate && (Children.Count > 0 || Files.Count > 0))
        {
            using (BeginBatchUpdate())
            {
                PropagateDownward(newState);
            }
        }

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

    private class BatchUpdateScope(DirectoryTreeNode node) : IDisposable
    {
        public void Dispose() => node.EndBatchUpdate();
    }
}
