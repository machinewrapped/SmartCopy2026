using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using SmartCopy.UI.ViewModels.Workflows;

namespace SmartCopy.UI.Views.Workflows;

public partial class SaveWorkflowDialog : Window
{
    private SaveWorkflowDialogViewModel? _vm;

    public SaveWorkflowDialog()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.OkRequested -= OnOk;
                _vm.CancelRequested -= OnCancel;
            }

            _vm = DataContext as SaveWorkflowDialogViewModel;

            if (_vm != null)
            {
                _vm.OkRequested += OnOk;
                _vm.CancelRequested += OnCancel;
            }
        };
    }

    private async void OnOk()
    {
        if (_vm is null) return;

        if (_vm.IsOverwrite)
        {
            var confirmVm = new ConfirmDialogViewModel
            {
                Title = "Confirm Replace",
                Message = $"Replace existing workflow \"{_vm.WorkflowName.Trim()}\"?",
                ConfirmText = "Replace"
            };
            var confirm = new ConfirmDialog { DataContext = confirmVm };

            var result = await confirm.ShowDialog<bool?>(this);
            if (result != true) return;
        }

        Dispatcher.UIThread.Post(() => Close(true));
    }

    private void OnCancel() => Dispatcher.UIThread.Post(() => Close(false));

}
