using Avalonia.Controls;
using SmartCopy.UI.ViewModels.Pipeline;

namespace SmartCopy.UI.Views.Pipeline;

public partial class EditStepDialog : Window
{
    private EditStepDialogViewModel? _viewModel;

    public EditStepDialog()
    {
        InitializeComponent();

        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.OkRequested -= OnOkRequested;
                _viewModel.CancelRequested -= OnCancelRequested;
            }

            _viewModel = DataContext as EditStepDialogViewModel;
            if (_viewModel is not null)
            {
                _viewModel.OkRequested += OnOkRequested;
                _viewModel.CancelRequested += OnCancelRequested;
            }
        };
    }

    private void OnOkRequested()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Close(true));
    }

    private void OnCancelRequested()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Close(false));
    }
}
