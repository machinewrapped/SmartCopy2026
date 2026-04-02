using Avalonia.Controls;
using Avalonia.Threading;
using SmartCopy.Core.Settings;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class ImportConflictDialog : Window
{
    private ImportConflictDialogViewModel? _vm;

    public ImportConflictDialog()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.Resolved -= OnResolved;
                _vm.CancelRequested -= OnCancel;
            }

            _vm = DataContext as ImportConflictDialogViewModel;

            if (_vm != null)
            {
                _vm.Resolved += OnResolved;
                _vm.CancelRequested += OnCancel;
            }
        };
    }

    private void OnResolved(ConflictResolution resolution) =>
        Dispatcher.UIThread.Post(() => Close(resolution));

    private void OnCancel() =>
        Dispatcher.UIThread.Post(() => Close(null));
}
