using Avalonia.Controls;
using Avalonia.Threading;
using SmartCopy.UI.ViewModels.Workflows;

namespace SmartCopy.UI.Views.Workflows;

public partial class RenameInputDialog : Window
{
    private RenameInputViewModel? _vm;

    public RenameInputDialog()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.OkRequested -= OnOk;
                _vm.CancelRequested -= OnCancel;
            }

            _vm = DataContext as RenameInputViewModel;

            if (_vm != null)
            {
                _vm.OkRequested += OnOk;
                _vm.CancelRequested += OnCancel;
            }
        };
    }

    private void OnOk() => Dispatcher.UIThread.Post(() => Close(true));
    private void OnCancel() => Dispatcher.UIThread.Post(() => Close(false));
}
