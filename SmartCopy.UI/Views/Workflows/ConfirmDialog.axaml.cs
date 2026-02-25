using Avalonia.Controls;
using Avalonia.Threading;
using SmartCopy.UI.ViewModels.Workflows;

namespace SmartCopy.UI.Views.Workflows;

public partial class ConfirmDialog : Window
{
    private ConfirmDialogViewModel? _vm;

    public ConfirmDialog()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.ConfirmRequested -= OnConfirm;
                _vm.CancelRequested -= OnCancel;
            }

            _vm = DataContext as ConfirmDialogViewModel;

            if (_vm != null)
            {
                _vm.ConfirmRequested += OnConfirm;
                _vm.CancelRequested += OnCancel;
            }
        };
    }

    private void OnConfirm() => Dispatcher.UIThread.Post(() => Close(true));
    private void OnCancel() => Dispatcher.UIThread.Post(() => Close(false));
}
