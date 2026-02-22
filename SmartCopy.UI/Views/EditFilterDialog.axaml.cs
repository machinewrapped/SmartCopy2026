using Avalonia.Controls;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class EditFilterDialog : Window
{
    private EditFilterDialogViewModel? _vm;

    public EditFilterDialog()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.OkRequested -= OnOk;
                _vm.CancelRequested -= OnCancel;
            }

            _vm = DataContext as EditFilterDialogViewModel;

            if (_vm != null)
            {
                _vm.OkRequested += OnOk;
                _vm.CancelRequested += OnCancel;
            }
        };
    }

    private void OnOk() => Close(true);
    private void OnCancel() => Close(false);
}
