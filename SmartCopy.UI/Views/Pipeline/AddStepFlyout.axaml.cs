using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SmartCopy.Core.Pipeline;
using SmartCopy.UI.ViewModels.Pipeline;

namespace SmartCopy.UI.Views.Pipeline;

public partial class AddStepFlyout : UserControl
{
    private AddStepViewModel? _vm;

    public AddStepFlyout()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChangedHandler;
    }

    private void OnDataContextChangedHandler(object? sender, EventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as AddStepViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AddStepViewModel.IsSavingPipeline) && _vm!.IsSavingPipeline)
            Dispatcher.UIThread.Post(() => PipelineNameTextBox.Focus(), DispatcherPriority.Loaded);
        else if (e.PropertyName is
            nameof(AddStepViewModel.IsLevel2Visible) or
            nameof(AddStepViewModel.IsLevel3Visible) or
            nameof(AddStepViewModel.IsSavingPipeline) or
            nameof(AddStepViewModel.IsLoadingPipeline))
            Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Loaded);
    }

    private void OnSelectStepTypeClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_vm is null || sender is not Control { DataContext: StepTypeItem item }) return;
        _vm.SelectStepTypeCommand.Execute(item.Kind);
    }

    private void OnPickPresetClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_vm is null || sender is not Control { DataContext: StepPresetItem item }) return;
        _vm.PickPresetCommand.Execute(item);
    }

    private void OnDeletePresetClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_vm is null || sender is not Control { DataContext: StepPresetItem item }) return;
        _vm.DeletePresetCommand.Execute(item);
    }

    private void OnLoadPresetClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_vm is null || sender is not Control { DataContext: PipelinePreset item }) return;
        _vm.LoadPresetCommand.Execute(item.Name);
    }

    private void OnDeletePipelineClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_vm is null || sender is not Control { DataContext: PipelinePreset item }) return;
        _vm.DeletePipelineCommand.Execute(item.Name);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is AddStepViewModel vm)
        {
            if (vm.IsLevel3Visible)
                vm.GoBackToLevel2Command.Execute(null);
            else if (vm.IsLevel2Visible)
                vm.GoBackCommand.Execute(null);
            else if (vm.IsSavingPipeline)
                vm.CancelSavePipelineCommand.Execute(null);
            else if (vm.IsLoadingPipeline)
                vm.CancelLoadPipelineCommand.Execute(null);
            else
                vm.CloseCommand.Execute(null);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
