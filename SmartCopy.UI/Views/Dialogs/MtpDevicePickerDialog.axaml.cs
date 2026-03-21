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
                vm.OkRequested += () => Close(vm.SelectedDevice);
                vm.CancelRequested += () => Close(null);
            }
        };
    }
}
