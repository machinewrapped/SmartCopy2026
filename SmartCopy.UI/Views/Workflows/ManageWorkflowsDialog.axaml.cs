using System.Threading.Tasks;
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

    private void OnCloseRequested() => Dispatcher.UIThread.Post(() => Close());

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
        var confirm = new Window
        {
            Title = "Confirm Delete",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.Transparent,
        };

        var panel = new StackPanel
        {
            Spacing = 12,
            Margin = new Avalonia.Thickness(20),
        };

        panel.Children.Add(new TextBlock
        {
            Text = $"Delete workflow \"{name}\"?",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
        };

        var cancelBtn = new Button { Content = "Cancel", Width = 80 };
        var deleteBtn = new Button { Content = "Delete", Width = 80 };

        cancelBtn.Click += (_, _) => confirm.Close(false);
        deleteBtn.Click += (_, _) => confirm.Close(true);

        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(deleteBtn);
        panel.Children.Add(buttonPanel);
        confirm.Content = panel;

        var result = await confirm.ShowDialog<bool?>(this);
        return result == true;
    }
}
