using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;

namespace SmartCopy.Core.DirectoryTree;

/// <summary>
/// Abstract base for all nodes in the directory tree (files and directories).
///
/// The node is stateful and indicates whether it is user-selected, included or excluded by filters,
/// whether it is marked for removal, and whether it is dirty.
/// </summary>
public abstract class DirectoryTreeNode : INotifyPropertyChanged
{
    private FileSystemNode _filesystemNode;
    private CheckState _checkState;

    protected DirectoryTreeNode(
        FileSystemNode filesystemNode,
        DirectoryNode? parent,
        CheckState checkState = CheckState.Unchecked)
    {
        _filesystemNode = filesystemNode;
        _checkState = checkState;

        Parent = parent;
        RelativePathSegments = parent is null ? Array.Empty<string>() : [.. parent.RelativePathSegments.Append(filesystemNode.Name)];
    }

    public string Name       => _filesystemNode.Name;
    public string FullPath   => _filesystemNode.FullPath;
    public bool IsDirectory  => _filesystemNode.IsDirectory;
    public long Size         => _filesystemNode.Size;
    public DateTime CreatedAt  => _filesystemNode.CreatedAt;
    public DateTime ModifiedAt => _filesystemNode.ModifiedAt;
    public FileAttributes Attributes => _filesystemNode.Attributes;

    /// <summary>
    /// The relative path segments from the root node to this node.
    /// </summary>
    public string[] RelativePathSegments { get; }

    /// <summary>
    /// The canonical relative path from the root node to this node.
    /// </summary>
    public string CanonicalRelativePath => string.Join("/", RelativePathSegments);

    public bool IsDirty { get; private set; } = false;

    public override string ToString() => CanonicalRelativePath + (IsDirectory ? "/" : "");

    public CheckState CheckState
    {
        get => _checkState;
        set
        {
            if (_checkState != value)
            {
                SetCheckState(value);

                if (value == CheckState.Checked)
                {
                    OnChecked();
                }
                else if (value == CheckState.Unchecked)
                {
                    OnUnchecked();
                }

                Parent?.RecalculateCheckState();
            }
        }
    }

    /// <summary>Called when CheckState transitions to Checked. Override to expand the node.</summary>
    protected virtual void OnChecked() { }

    /// <summary>Called when CheckState transitions to Unchecked.</summary>
    protected virtual void OnUnchecked() { }

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
                OnPropertyChanged(nameof(FilterResult));
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

    public bool IsSelected         => CheckState == CheckState.Checked && FilterResult == FilterResult.Included && !IsMarkedForRemoval;
    public bool IsFilterIncluded   => FilterResult != FilterResult.Excluded;
    public bool IsAtomicIncluded   => FilterResult == FilterResult.Included;

    private string _notes = string.Empty;
    public string Notes
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

    public DirectoryNode? Parent { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void MarkDirty()
    {
        if (IsDirty) return;

        IsDirty = true;
        OnPropertyChanged(nameof(IsDirty));
        Parent?.MarkDirty();
    }

    public void ClearDirty() => IsDirty = false;

    public void UpdateFrom(FileSystemNode filesystemNode)
    {
        if (IsDirectory != filesystemNode.IsDirectory)
        {
            throw new InvalidOperationException("Cannot update a node with a different node type.");
        }

        if (!string.Equals(FullPath, filesystemNode.FullPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot update a node with a different full path.");
        }

        _filesystemNode = filesystemNode;
        MarkDirty();
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(FullPath));
        OnPropertyChanged(nameof(Size));
        OnPropertyChanged(nameof(CreatedAt));
        OnPropertyChanged(nameof(ModifiedAt));
        OnPropertyChanged(nameof(Attributes));
    }

    public abstract void MarkForRemoval();

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // ── CheckState propagation helpers ───────────────────────────────────────

    /// <summary>
    /// Set this node only (no upward/downward recursion, no hooks).
    /// </summary>
    internal void SetCheckState(CheckState newState)
    {
        if (_checkState == newState)
            return;

        _checkState = newState;
        MarkDirty();
        OnPropertyChanged(nameof(CheckState));
        OnPropertyChanged(nameof(IsSelected));
    }
}
