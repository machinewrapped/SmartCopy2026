using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using SmartCopy.UI.ViewModels;
using SmartCopy.UI.ViewModels.Pipeline;
using SmartCopy.UI.Views.Pipeline;

namespace SmartCopy.UI.Views;

public partial class PipelineView : UserControl
{
    private PipelineViewModel? _currentViewModel;

    public PipelineView()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (_currentViewModel != null)
            {
                _currentViewModel.Steps.CollectionChanged -= Steps_CollectionChanged;
                _currentViewModel.EditStepRequested -= OnEditStepRequested;
                _currentViewModel.AddStep.StepTypeSelected -= OnAddStepTypeSelected;
                _currentViewModel.AddStep.CloseRequested -= OnCloseRequestedClosePopup;
            }

            _currentViewModel = DataContext as PipelineViewModel;

            if (_currentViewModel != null)
            {
                _currentViewModel.Steps.CollectionChanged += Steps_CollectionChanged;
                _currentViewModel.EditStepRequested += OnEditStepRequested;
                _currentViewModel.AddStep.StepTypeSelected += OnAddStepTypeSelected;
                _currentViewModel.AddStep.CloseRequested += OnCloseRequestedClosePopup;
            }
        };
    }

    private void OnAddStepButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _currentViewModel?.AddStep.GoBackCommand.Execute(null);
        AddStepPopup.IsOpen = true;
    }

    private void OnCloseRequestedClosePopup() => Dispatcher.UIThread.Post(() => AddStepPopup.IsOpen = false);

    private async void OnAddStepTypeSelected(StepKind kind)
    {
        if (_currentViewModel is null || this.VisualRoot is not Window parentWindow)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => AddStepPopup.IsOpen = false);

        var vm = EditStepDialogViewModel.ForNew(kind);
        var dialog = new EditStepDialog { DataContext = vm };
        var result = await dialog.ShowDialog<bool?>(parentWindow);
        if (result == true && vm.ResultStep is not null)
        {
            _currentViewModel.AddStepFromResult(kind, vm.ResultStep);
        }
    }

    private async void OnEditStepRequested(object? sender, PipelineStepViewModel step)
    {
        if (_currentViewModel is null || this.VisualRoot is not Window parentWindow)
        {
            return;
        }

        var vm = EditStepDialogViewModel.ForEdit(step);
        var dialog = new EditStepDialog { DataContext = vm };
        var result = await dialog.ShowDialog<bool?>(parentWindow);
        if (result == true && vm.ResultStep is not null)
        {
            _currentViewModel.ReplaceStep(step, vm.ResultStep);
        }
    }

    private void Steps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(
                () => StepsScrollViewer?.ScrollToEnd(),
                DispatcherPriority.Loaded);
        }
    }
}
