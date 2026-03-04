using System;
using Avalonia.Controls;

namespace SmartCopy.UI.Views.Dialogs;

public partial class SaveFilterChainDialog : Window
{
    private ViewModels.Dialogs.SaveFilterChainDialogViewModel? _vm;

    public SaveFilterChainDialog()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.OkRequested -= OnOkRequested;
                _vm.CancelRequested -= OnCancelRequested;
            }

            _vm = DataContext as ViewModels.Dialogs.SaveFilterChainDialogViewModel;

            if (_vm != null)
            {
                _vm.OkRequested += OnOkRequested;
                _vm.CancelRequested += OnCancelRequested;
            }
        };
    }

    private void OnOkRequested() => Close(true);
    private void OnCancelRequested() => Close(false);
}
