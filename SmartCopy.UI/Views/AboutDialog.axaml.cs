using Avalonia.Controls;
using Avalonia.Threading;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class AboutDialog : Window
{
    private AboutDialogViewModel? _vm;

    public AboutDialog()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
                _vm.CloseRequested -= OnClose;

            _vm = DataContext as AboutDialogViewModel;

            if (_vm is not null)
                _vm.CloseRequested += OnClose;
        };
    }

    private void OnClose() => Dispatcher.UIThread.Post(Close);
}
