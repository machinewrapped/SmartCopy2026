using Avalonia.Controls;

namespace SmartCopy.UI.Views.FilterEditors;

public partial class ExtensionFilterEditorView : UserControl
{
    public ExtensionFilterEditorView()
    {
        InitializeComponent();
    }

    private void OnRemoveExtensionClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button { DataContext: string extension })
        {
            if (DataContext is SmartCopy.UI.ViewModels.Filters.ExtensionFilterEditorViewModel vm)
            {
                vm.RemoveExtensionCommand.Execute(extension);
                e.Handled = true;
            }
        }
    }
}
