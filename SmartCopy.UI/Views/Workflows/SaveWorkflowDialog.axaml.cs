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
            var confirm = new Window
            {
                Title = "Confirm Replace",
                Width = 360,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Avalonia.Media.Brushes.Transparent,
                Content = BuildConfirmContent(_vm.WorkflowName.Trim())
            };

            var result = await confirm.ShowDialog<bool?>(this);
            if (result != true) return;
        }

        Dispatcher.UIThread.Post(() => Close(true));
    }

    private void OnCancel() => Dispatcher.UIThread.Post(() => Close(false));

    private static StackPanel BuildConfirmContent(string name)
    {
        var panel = new StackPanel
        {
            Spacing = 12,
            Margin = new Avalonia.Thickness(20),
        };

        panel.Children.Add(new TextBlock
        {
            Text = $"Replace existing workflow \"{name}\"?",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        var buttonPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
        };

        var cancelBtn = new Button { Content = "Cancel", Width = 80 };
        var replaceBtn = new Button { Content = "Replace", Width = 80 };

        cancelBtn.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(cancelBtn) is Window w)
                w.Close(false);
        };
        replaceBtn.Click += (_, _) =>
        {
            if (TopLevel.GetTopLevel(replaceBtn) is Window w)
                w.Close(true);
        };

        buttonPanel.Children.Add(cancelBtn);
        buttonPanel.Children.Add(replaceBtn);
        panel.Children.Add(buttonPanel);

        return panel;
    }
}
