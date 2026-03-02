using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SmartCopy.UI.ViewModels.Pipeline;

namespace SmartCopy.UI.Views.Pipeline.StepEditors;

public partial class CopyMoveStepEditor : UserControl
{
    public CopyMoveStepEditor()
    {
        InitializeComponent();
    }

    private async void OnBrowseDestinationClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not TopLevel topLevel)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select destination folder",
            AllowMultiple = false,
        });

        if (folders is not { Count: > 0 })
            return;

        var selectedUri = folders[0].Path;
        if (!selectedUri.IsAbsoluteUri || !selectedUri.IsFile)
            return;

        var selectedPath = selectedUri.LocalPath;

        if (DataContext is IHasDestinationPath destinationPathVm)
        {
            destinationPathVm.DestinationPath = selectedPath;
        }
    }
}
