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

    private void OnSavePipelineMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddStepViewModel vm)
            return;

        vm.SavePipelineCommand.Execute(null);
    }
}
