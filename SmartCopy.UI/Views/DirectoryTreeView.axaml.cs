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
    public DirectoryTreeView()
    {
        InitializeComponent();
        DirectoryTree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
        DirectoryTree.AddHandler(PointerPressedEvent, OnTreePointerPressed, RoutingStrategies.Tunnel);
    }

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(DirectoryTree).Properties.IsLeftButtonPressed == false)
        {
            return;
        }

        var treeViewItem = e.Source as TreeViewItem
            ?? (e.Source as Visual)?.GetVisualAncestors().OfType<TreeViewItem>().FirstOrDefault();
        if (treeViewItem is null)
        {
            return;
        }

        var expander = treeViewItem.GetVisualDescendants()
            .OfType<ToggleButton>()
            .FirstOrDefault(button => button.Name == "PART_ExpandCollapseChevron");

        if (expander?.IsVisible != true || expander.TranslatePoint(default, DirectoryTree) is not { } position)
        {
            return;
        }

        const double hitTargetPadding = 6;
        var pointer = e.GetPosition(DirectoryTree);
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
    /// Space toggles the checkbox on the focused tree node.
    /// Avalonia's TreeView does not forward Space to the CheckBox in the item template.
    /// Alt+Right/Left recursively expand/collapse the selected node.
    /// </summary>
    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            if (DirectoryTree.SelectedItem is DirectoryTreeNode { IsFilterIncluded: true } node)
            {
                node.CheckState = node.CheckState == CheckState.Checked
                    ? CheckState.Unchecked
                    : CheckState.Checked;
                e.Handled = true;
            }
            return;
        }

        if (e.KeyModifiers == KeyModifiers.Alt && DirectoryTree.SelectedItem is DirectoryNode dirNode)
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
