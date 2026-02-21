using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SmartCopy.Core.FileSystem;

public class FileSystemNode : INotifyPropertyChanged
{
    // Filesystem data (immutable after scan)
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ModifiedAt { get; init; }
    public FileAttributes Attributes { get; init; }

    private int _batchUpdateDepth = 0;

    private CheckState _checkState = CheckState.Unchecked;
    public CheckState CheckState
    {
        get => _checkState;
        set
        {
            CheckState effectiveValue = value;
            
            // Intercept UI clicks to enforce Unchecked/Indeterminate -> Checked -> Unchecked
            // Avalonia IsThreeState=True cycles: Unchecked -> Checked -> Indeterminate -> Unchecked
            // If we are currently Checked and UI tries to set Indeterminate, override to Unchecked.
            if (_checkState == CheckState.Checked && value == CheckState.Indeterminate)
            {
                effectiveValue = CheckState.Unchecked;
            }
            // If we are currently Indeterminate and UI tries to set Unchecked, override to Checked.
            else if (_checkState == CheckState.Indeterminate && value == CheckState.Unchecked)
            {
                effectiveValue = CheckState.Checked;
            }

            if (_checkState != effectiveValue)
            {
                SetCheckStateWithPropagation(effectiveValue);
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

    public FileSystemNode? Parent { get; init; }
    public ObservableCollection<FileSystemNode> Children { get; } = new();
    public ObservableCollection<FileSystemNode> Files { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (_batchUpdateDepth == 0)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public IDisposable BeginBatchUpdate()
    {
        _batchUpdateDepth++;
        return new BatchUpdateScope(this);
    }

    private void EndBatchUpdate()
    {
        _batchUpdateDepth--;
        if (_batchUpdateDepth == 0)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); // Reset bindings
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
            if (child._checkState != newState)
            {
                child._checkState = newState;
                child.OnPropertyChanged(nameof(CheckState));
                child.OnPropertyChanged(nameof(IsSelected));
                if (child.Children.Count > 0 || child.Files.Count > 0)
                {
                    child.PropagateDownward(newState);
                }
            }
        }

        foreach (var file in Files)
        {
            if (file._checkState != newState)
            {
                file._checkState = newState;
                file.OnPropertyChanged(nameof(CheckState));
                file.OnPropertyChanged(nameof(IsSelected));
            }
        }
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

        if (_checkState != computedState)
        {
            _checkState = computedState;
            OnPropertyChanged(nameof(CheckState));
            OnPropertyChanged(nameof(IsSelected));
            Parent?.RecalculateCheckState();
        }
    }

    private class BatchUpdateScope : IDisposable
    {
        private readonly FileSystemNode _node;
        public BatchUpdateScope(FileSystemNode node) => _node = node;
        public void Dispose() => _node.EndBatchUpdate();
    }
}
