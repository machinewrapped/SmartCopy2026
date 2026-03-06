using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SmartCopy.Core.Workflows;
using SmartCopy.UI.ViewModels.Workflows;

namespace SmartCopy.UI.Views.Workflows;

public partial class ManageWorkflowsDialog : Window
{
    private ManageWorkflowsDialogViewModel? _vm;

    public ManageWorkflowsDialog()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (_vm != null)
            {
                _vm.CloseRequested -= OnCloseRequested;
                _vm.RenameInputRequested -= OnRenameInput;
                _vm.DeleteConfirmRequested -= OnDeleteConfirm;
            }

            _vm = DataContext as ManageWorkflowsDialogViewModel;

            if (_vm != null)
            {
                _vm.CloseRequested += OnCloseRequested;
                _vm.RenameInputRequested += OnRenameInput;
                _vm.DeleteConfirmRequested += OnDeleteConfirm;
            }
        };
    }

    private void OnTitleBarPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void OnCloseRequested() => Dispatcher.UIThread.Post(() => Close());

    private void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WorkflowPreset preset } && _vm is not null)
        {
            _vm.LoadWorkflowCommand.Execute(preset);
            e.Handled = true;
        }
    }

    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WorkflowPreset preset } && _vm is not null)
        {
            _vm.RenameWorkflowCommand.Execute(preset);
            e.Handled = true;
        }
    }

    private void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: WorkflowPreset preset } && _vm is not null)
        {
            _vm.DeleteWorkflowCommand.Execute(preset);
            e.Handled = true;
        }
    }

    private async Task<string?> OnRenameInput(string currentName)
    {
        var inputVm = new RenameInputViewModel { NewName = currentName };
        var dialog = new RenameInputDialog { DataContext = inputVm };
        var result = await dialog.ShowDialog<bool?>(this);
        return result == true ? inputVm.NewName?.Trim() : null;
    }

    private async Task<bool> OnDeleteConfirm(string name)
    {
        var vm = new ConfirmDialogViewModel
        {
            Title = "Confirm Delete",
            Message = $"Delete workflow \"{name}\"?",
            ConfirmText = "Delete"
        };
        var confirm = new ConfirmDialog { DataContext = vm };
        var result = await confirm.ShowDialog<bool?>(this);
        return result == true;
    }
}
