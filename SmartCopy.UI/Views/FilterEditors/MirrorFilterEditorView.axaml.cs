using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SmartCopy.UI.ViewModels.Filters;

namespace SmartCopy.UI.Views.FilterEditors;

public partial class MirrorFilterEditorView : UserControl
{
    public MirrorFilterEditorView()
    {
        InitializeComponent();
    }

    private async void OnBrowseComparisonPathClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not TopLevel topLevel)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select comparison folder",
            AllowMultiple = false,
        });

        if (folders is not { Count: > 0 })
            return;

        var selectedUri = folders[0].Path;
        if (!selectedUri.IsAbsoluteUri || !selectedUri.IsFile)
            return;

        if (DataContext is MirrorFilterEditorViewModel vm)
        {
            vm.ComparisonPath = selectedUri.LocalPath;
        }
    }
}
