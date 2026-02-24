using Avalonia.Controls;
using Avalonia.Interactivity;
using SmartCopy.UI.ViewModels.Pipeline;

namespace SmartCopy.UI.Views.Pipeline;

public partial class AddStepFlyout : UserControl
{
    public AddStepFlyout()
    {
        InitializeComponent();
    }

    private void OnPipelinePresetMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddStepViewModel vm || sender is not MenuItem menuItem)
            return;

        if (menuItem.CommandParameter is not string presetName || string.IsNullOrWhiteSpace(presetName))
            return;

        vm.LoadPresetCommand.Execute(presetName);
    }

    private void OnDeletePipelineClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true; // Prevent the menu item from handling it (which would trigger load)

        if (DataContext is not AddStepViewModel vm || sender is not Button button)
            return;

        if (button.CommandParameter is not string pipelineName || string.IsNullOrWhiteSpace(pipelineName))
            return;

        vm.DeletePipelineCommand.Execute(pipelineName);
    }
}
