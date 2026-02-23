using Avalonia.Controls;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class PreviewView : Window
{
    private PreviewViewModel? _viewModel;

    public PreviewView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.RunRequested -= OnRunRequested;
                _viewModel.CancelRequested -= OnCancelRequested;
            }

            _viewModel = DataContext as PreviewViewModel;
            if (_viewModel is not null)
            {
                _viewModel.RunRequested += OnRunRequested;
                _viewModel.CancelRequested += OnCancelRequested;
            }
        };
    }

    private void OnRunRequested()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Close(true));
    }

    private void OnCancelRequested()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Close(false));
    }
}
