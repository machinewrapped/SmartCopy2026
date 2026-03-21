using System.Runtime.Versioning;
using Avalonia.Controls;

namespace SmartCopy.UI.Views.Dialogs;

public partial class MtpDevicePickerDialog : Window
{
    [SupportedOSPlatform("windows")]
    public MtpDevicePickerDialog()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is ViewModels.Dialogs.MtpDevicePickerViewModel vm)
            {
                vm.OkRequested += () => Close(vm.Result);
                vm.CancelRequested += () => Close(null);
            }
        };
    }

    [SupportedOSPlatform("windows")]
    private async void OnItemSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb || lb.SelectedItem is not ViewModels.Dialogs.MtpPickerItem item) return;
        lb.SelectedItem = null;
        if (DataContext is ViewModels.Dialogs.MtpDevicePickerViewModel vm)
            await vm.NavigateIntoAsync(item);
    }
}
