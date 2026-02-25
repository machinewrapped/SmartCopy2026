using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
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
                _currentViewModel.AddStep.StepPresetPicked -= OnStepPresetPickedClosePopup;
                _currentViewModel.AddStep.CloseRequested -= OnCloseRequestedClosePopup;
            }

            _currentViewModel = DataContext as PipelineViewModel;

            if (_currentViewModel != null)
            {
                _currentViewModel.Steps.CollectionChanged += Steps_CollectionChanged;
                _currentViewModel.EditStepRequested += OnEditStepRequested;
                _currentViewModel.AddStep.StepTypeSelected += OnAddStepTypeSelected;
                _currentViewModel.AddStep.StepPresetPicked += OnStepPresetPickedClosePopup;
                _currentViewModel.AddStep.CloseRequested += OnCloseRequestedClosePopup;
            }
        };
    }

    private void OnAddStepButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _currentViewModel?.AddStep.GoBackCommand.Execute(null);
        AddStepPopup.IsOpen = true;
        Dispatcher.UIThread.Post(() => AddStepFlyoutControl.Focus(), DispatcherPriority.Loaded);
    }

    private void OnCloseRequestedClosePopup() => Dispatcher.UIThread.Post(() => AddStepPopup.IsOpen = false);

    private void OnStepPresetPickedClosePopup(StepPreset _) =>
        Dispatcher.UIThread.Post(() => AddStepPopup.IsOpen = false);

    private async void OnAddStepTypeSelected(StepKind kind)
    {
        if (_currentViewModel is null || this.VisualRoot is not Window parentWindow)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => AddStepPopup.IsOpen = false);

        var vm = EditStepDialogViewModel.ForNew(kind, _currentViewModel.AppSettings);
        var dialog = new EditStepDialog { DataContext = vm };
        var result = await dialog.ShowDialog<bool?>(parentWindow);
        if (result == true && vm.ResultStep is not null)
        {
            if (vm.SaveAsPreset && !string.IsNullOrWhiteSpace(vm.StepName))
            {
                await SaveStepPresetAsync(kind, vm.ResultStep, vm.StepName);
            }

            var destPath = (vm.ResultStep as CopyStep)?.DestinationPath
                ?? (vm.ResultStep as MoveStep)?.DestinationPath;
            if (destPath is not null)
                _currentViewModel.RecordRecentTarget(destPath);

            _currentViewModel.AddStepFromResult(kind, vm.ResultStep, vm.ResultCustomName);
        }
    }

    private async void OnEditStepRequested(object? sender, PipelineStepViewModel step)
    {
        if (_currentViewModel is null || this.VisualRoot is not Window parentWindow)
        {
            return;
        }

        var vm = EditStepDialogViewModel.ForEdit(step, _currentViewModel.AppSettings);
        var dialog = new EditStepDialog { DataContext = vm };
        var result = await dialog.ShowDialog<bool?>(parentWindow);
        if (result == true && vm.ResultStep is not null)
        {
            if (vm.SaveAsPreset && !string.IsNullOrWhiteSpace(vm.StepName))
            {
                await SaveStepPresetAsync(step.Kind, vm.ResultStep, vm.StepName);
            }

            var destPath = (vm.ResultStep as CopyStep)?.DestinationPath
                ?? (vm.ResultStep as MoveStep)?.DestinationPath;
            if (destPath is not null)
                _currentViewModel.RecordRecentTarget(destPath);

            _currentViewModel.ReplaceStep(step, vm.ResultStep, vm.ResultCustomName);
        }
    }

    private async System.Threading.Tasks.Task SaveStepPresetAsync(
        StepKind kind,
        ITransformStep step,
        string name)
    {
        if (_currentViewModel is null) return;

        var preset = new StepPreset { Name = name, Config = step.Config };
        await _currentViewModel.StepPresetStore.SaveUserPresetAsync(kind.ToString(), preset);
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
