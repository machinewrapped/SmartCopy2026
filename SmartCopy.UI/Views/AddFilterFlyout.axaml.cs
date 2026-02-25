using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class AddFilterFlyout : UserControl
{
    private AddFilterViewModel? _vm;

    public AddFilterFlyout()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChangedHandler;
    }

    private void OnDataContextChangedHandler(object? sender, EventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as AddFilterViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AddFilterViewModel.IsLevel2Visible))
            Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Loaded);
    }

    private void OnSelectFilterTypeClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_vm is null || sender is not Control { DataContext: FilterTypeItem item }) return;
        _vm.SelectFilterTypeCommand.Execute(item);
    }

    private void OnPickPresetClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_vm is null || sender is not Control { DataContext: PresetItem item }) return;
        _vm.PickPresetCommand.Execute(item);
    }

    private void OnDeletePresetClick(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_vm is null || sender is not Control { DataContext: PresetItem item }) return;
        _vm.DeletePresetCommand.Execute(item);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is AddFilterViewModel vm)
        {
            if (vm.IsLevel2Visible)
                vm.GoBackCommand.Execute(null);
            else
                vm.CloseCommand.Execute(null);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
