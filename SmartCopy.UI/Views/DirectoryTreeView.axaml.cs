using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class DirectoryTreeView : UserControl
{
    private DirectoryTreeViewModel? _viewModel;

    /// <summary>The node under the last pointer press, preferred as the active node when it becomes selected.</summary>
    private DirectoryTreeNode? _pressedNode;

    public DirectoryTreeView()
    {
        InitializeComponent();
        DirectoryTree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
        DirectoryTree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
        DirectoryTree.SelectionChanged += OnTreeSelectionChanged;
    }

    /// <summary>
    /// <see cref="DirectoryTreeViewModel.SelectedNode"/> is synced here rather than bound to
    /// <see cref="TreeView.SelectedItem"/>: in multi-select mode the TreeView keeps SelectedItem on the
    /// first selected item, and assigning it collapses the selection to that one item.
    /// </summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as DirectoryTreeViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            SyncTreeSelectionFromViewModel();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DirectoryTreeViewModel.SelectedNode))
        {
            SyncTreeSelectionFromViewModel();
        }
    }

    private void SyncTreeSelectionFromViewModel()
    {
        // A node set by the view model (initial root, watcher retarget) replaces the selection,
        // unless it is already part of it — which is the case when the change came from the tree.
        if (_viewModel?.SelectedNode is { } node && !DirectoryTree.SelectedItems.Contains(node))
        {
            DirectoryTree.SelectedItem = node;
        }
    }

    private void OnTreeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var pressedNode = _pressedNode;
        _pressedNode = null;

        if (_viewModel is null)
        {
            return;
        }

        // Prefer the clicked node while it remains selected, even when it was already selected
        // (e.g. a Shift+Click range that ends on it only adds the nodes in between).
        if (pressedNode is not null && DirectoryTree.SelectedItems.Contains(pressedNode))
        {
            _viewModel.SelectedNode = pressedNode;
        }
        else if (e.AddedItems.OfType<DirectoryTreeNode>().LastOrDefault() is { } lastAdded)
        {
            _viewModel.SelectedNode = lastAdded;
        }
        else if (_viewModel.SelectedNode is not { } current || !DirectoryTree.SelectedItems.Contains(current))
        {
            // The active node was deselected (or the tree cleared): fall back to the most recent
            // remaining selection, or none.
            _viewModel.SelectedNode = DirectoryTree.SelectedItems.OfType<DirectoryTreeNode>().LastOrDefault();
        }
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(DirectoryTree).Properties.IsLeftButtonPressed == false)
        {
            return;
        }

        _pressedNode = (e.Source as Visual)?.GetSelfAndVisualAncestors()
            .OfType<TreeViewItem>().FirstOrDefault()?.DataContext as DirectoryTreeNode;

        if (e.Source is Visual visualSource
            && (visualSource is CheckBox || visualSource.GetVisualAncestors().OfType<CheckBox>().Any()))
        {
            return;
        }

        var treeViewItem = e.Source as TreeViewItem
            ?? (e.Source as Visual)?.GetVisualAncestors().OfType<TreeViewItem>().FirstOrDefault();
        if (treeViewItem is null)
        {
            return;
        }

        var expander = FindExpander(treeViewItem);

        if (expander?.IsVisible != true || expander.TranslatePoint(default, DirectoryTree) is not { } position)
        {
            return;
        }

        const double hitTargetPadding = 6;
        var pointer = e.GetPosition(DirectoryTree);
        var isDirectClick = pointer.X >= position.X
            && pointer.X <= position.X + expander.Bounds.Width
            && pointer.Y >= position.Y
            && pointer.Y <= position.Y + expander.Bounds.Height;
        if (isDirectClick)
        {
            return;
        }

        if (pointer.X < position.X - hitTargetPadding
            || pointer.X > position.X + expander.Bounds.Width + hitTargetPadding
            || pointer.Y < position.Y - hitTargetPadding
            || pointer.Y > position.Y + expander.Bounds.Height + hitTargetPadding)
        {
            return;
        }

        treeViewItem.IsExpanded = !treeViewItem.IsExpanded;
        e.Handled = true;
    }

    private static ToggleButton? FindExpander(Visual visual)
    {
        if (visual is ToggleButton { Name: "PART_ExpandCollapseChevron" } button)
        {
            return button;
        }

        foreach (var child in visual.GetVisualChildren())
        {
            if (child is TreeViewItem)
            {
                continue;
            }

            var expander = FindExpander(child);
            if (expander is not null)
            {
                return expander;
            }
        }

        return null;
    }

    private void OnSetAsSourcePathClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: DirectoryTreeNode node }
            && DataContext is DirectoryTreeViewModel vm)
        {
            vm.RequestSetAsSourcePath(node.FullPath);
            e.Handled = true;
        }
    }

    private void OnExpandAllClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: DirectoryNode node })
        {
            node.ExpandAll();
            e.Handled = true;
        }
    }

    private void OnCollapseAllClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { DataContext: DirectoryNode node })
        {
            node.CollapseAll();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Space toggles the checkboxes of the selected tree nodes: all are unchecked if every one
    /// is already checked, otherwise all are checked.
    /// Avalonia's TreeView does not forward Space to the CheckBox in the item template.
    /// Alt+Right/Left recursively expand/collapse the selected node.
    /// </summary>
    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && e.KeyModifiers == KeyModifiers.None)
        {
            var nodes = DirectoryTree.SelectedItems.OfType<DirectoryTreeNode>()
                .Where(n => n.IsFilterIncluded)
                .ToList();
            if (nodes.Count > 0)
            {
                var newState = nodes.All(n => n.CheckState == CheckState.Checked)
                    ? CheckState.Unchecked
                    : CheckState.Checked;
                foreach (var node in nodes)
                {
                    node.CheckState = newState;
                }
                e.Handled = true;
            }
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Alt && _viewModel?.SelectedNode is DirectoryNode dirNode)
        {
            switch (e.Key)
            {
                case Key.Right:
                    dirNode.ExpandAll();
                    e.Handled = true;
                    break;
                case Key.Left:
                    dirNode.CollapseAll();
                    e.Handled = true;
                    break;
            }
        }
    }
}
