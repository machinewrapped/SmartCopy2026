using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class DirectoryTreeView : UserControl
{
    public DirectoryTreeView()
    {
        InitializeComponent();
        DirectoryTree.AddHandler(KeyDownEvent, OnTreeKeyDown, RoutingStrategies.Tunnel);
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
            if (e.Key == Key.Right)
            {
                dirNode.ExpandAll();
                e.Handled = true;
            }
            else if (e.Key == Key.Left)
            {
                dirNode.CollapseAll();
                e.Handled = true;
            }
        }
    }
}
