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

    /// <summary>
    /// Space toggles the checkbox on the focused tree node.
    /// Avalonia's TreeView does not forward Space to the CheckBox in the item template.
    /// </summary>
    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;

        if (DirectoryTree.SelectedItem is DirectoryTreeNode { IsFilterIncluded: true } node)
        {
            node.CheckState = node.CheckState == CheckState.Checked
                ? CheckState.Unchecked
                : CheckState.Checked;
            e.Handled = true;
        }
    }
}
