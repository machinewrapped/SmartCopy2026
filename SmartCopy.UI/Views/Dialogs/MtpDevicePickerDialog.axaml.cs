using Avalonia.Controls;

namespace SmartCopy.UI.Views.Dialogs;

public partial class MtpDevicePickerDialog : Window
{
    public MtpDevicePickerDialog()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
#if WINDOWS
            if (DataContext is SmartCopy.UI.ViewModels.Dialogs.MtpDevicePickerViewModel vm)
            {
                vm.OkRequested += () => Close(vm.SelectedDevice);
                vm.CancelRequested += () => Close(null);
            }
#endif
        };
    }
}
