using Avalonia.Controls;
using Avalonia.Input;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.UI.Views;

public partial class DirectoryTreeView : UserControl
{
    public DirectoryTreeView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Space toggles the checkbox on the focused tree node.
    /// Avalonia's TreeView does not forward Space to the CheckBox in the item template.
    /// </summary>
    private void OnTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Space) return;

        if (DirectoryTree.SelectedItem is FileSystemNode { IsFilterIncluded: true } node)
        {
            node.CheckState = node.CheckState == CheckState.Checked
                ? CheckState.Unchecked
                : CheckState.Checked;
            e.Handled = true;
        }
    }
}
