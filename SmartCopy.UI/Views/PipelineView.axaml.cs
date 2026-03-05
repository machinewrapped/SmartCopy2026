using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;
using SmartCopy.UI.ViewModels;
using SmartCopy.UI.ViewModels.Dialogs;
using SmartCopy.UI.ViewModels.Pipeline;
using SmartCopy.UI.Views.Dialogs;
using SmartCopy.UI.Views.Pipeline;

namespace SmartCopy.UI.Views;

public partial class PipelineView : UserControl
{
    private PipelineViewModel? _currentViewModel;
    private AppSettings _appSettings;

    public PipelineView(AppSettings appSettings)
    {
        _appSettings = appSettings;

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
                _currentViewModel.SavePipelineRequested -= OnSavePipelineRequested;
            }

            _currentViewModel = DataContext as PipelineViewModel;

            if (_currentViewModel != null)
            {
                _currentViewModel.Steps.CollectionChanged += Steps_CollectionChanged;
                _currentViewModel.EditStepRequested += OnEditStepRequested;
                _currentViewModel.AddStep.StepTypeSelected += OnAddStepTypeSelected;
                _currentViewModel.AddStep.StepPresetPicked += OnStepPresetPickedClosePopup;
                _currentViewModel.AddStep.CloseRequested += OnCloseRequestedClosePopup;
                _currentViewModel.SavePipelineRequested += OnSavePipelineRequested;
            }
        };
    }

    private void OnAddStepButtonClick(object? sender, RoutedEventArgs e)
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
        if (_currentViewModel is null)
            return;

        Dispatcher.UIThread.Post(() => AddStepPopup.IsOpen = false);

        var step = StepEditorViewModelFactory.Create(kind, _appSettings).BuildStep();
        if (!step.IsConfigurable)
        {
            _currentViewModel.AddStepFromResult(kind, step);
            return;
        }

        if (this.VisualRoot is not Window parentWindow)
            return;

        var vm = EditStepDialogViewModel.ForNew(kind, _currentViewModel.AppSettings);
        var dialog = new EditStepDialog { DataContext = vm };
        var result = await dialog.ShowDialog<bool?>(parentWindow);
        if (result == true && vm.ResultStep is not null)
        {
            if (vm.SaveAsPreset && !string.IsNullOrWhiteSpace(vm.StepName))
            {
                await SaveStepPresetAsync(kind, vm.ResultStep, vm.StepName);
            }

            var destPath = (vm.ResultStep as IHasDestinationPath)?.DestinationPath;

            if (destPath is not null)
            {
                _currentViewModel.RecordRecentTarget(destPath);
            }

            _currentViewModel.AddStepFromResult(kind, vm.ResultStep, vm.ResultCustomName);
        }
    }

    private async void OnEditStepRequested(object? sender, PipelineStepViewModel step)
    {
        if (_currentViewModel is null || this.VisualRoot is not Window parentWindow)
            return;

        if (!step.Step.IsConfigurable)
            return;

        var vm = EditStepDialogViewModel.ForEdit(step, _currentViewModel.AppSettings);
        var dialog = new EditStepDialog { DataContext = vm };
        var result = await dialog.ShowDialog<bool?>(parentWindow);
        if (result == true && vm.ResultStep is not null)
        {
            if (vm.SaveAsPreset && !string.IsNullOrWhiteSpace(vm.StepName))
            {
                await SaveStepPresetAsync(step.Kind, vm.ResultStep, vm.StepName);
            }

            var destPath = (vm.ResultStep as IHasDestinationPath)?.DestinationPath;
            if (destPath is not null)
            {
                _currentViewModel.RecordRecentTarget(destPath);
            }

            _currentViewModel.ReplaceStep(step, vm.ResultStep, vm.ResultCustomName);
        }
    }

    private async Task SaveStepPresetAsync(
        StepKind kind,
        IPipelineStep step,
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

    private void OnLoadPresetButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string name })
        {
            _currentViewModel?.LoadPresetCommand?.Execute(name);
            this.Get<Button>("LoadPresetButton")?.Flyout?.Hide();
        }
    }

    private async void OnDeletePipelinePresetButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string name } && _currentViewModel != null)
        {
            this.Get<Button>("LoadPresetButton")?.Flyout?.Hide();

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is not Window window) return;

            var confirmVm = new SmartCopy.UI.ViewModels.Workflows.ConfirmDialogViewModel
            {
                Title = "Confirm Delete",
                Message = $"Delete pipeline \"{name}\" permanently?",
                ConfirmText = "Delete"
            };
            var confirm = new SmartCopy.UI.Views.Workflows.ConfirmDialog { DataContext = confirmVm };
            var confirmResult = await confirm.ShowDialog<bool?>(window);
            if (confirmResult == true)
            {
                await _currentViewModel.DeletePipelinePresetAsync(name);
            }
        }
    }

    private async void OnSavePipelineRequested(object? sender, EventArgs e)
    {
        if (_currentViewModel is null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not Window window) return;

        var dialogVm = new SavePipelineDialogViewModel();
        foreach (var preset in _currentViewModel.UserPresets)
        {
            dialogVm.ExistingNames.Add(preset.Name);
        }

        var dialog = new SavePipelineDialog
        {
            DataContext = dialogVm
        };

        var result = await dialog.ShowDialog<bool>(window);
        if (result && !string.IsNullOrWhiteSpace(dialogVm.PipelineName))
        {
            if (dialogVm.IsOverwrite)
            {
                var confirmVm = new SmartCopy.UI.ViewModels.Workflows.ConfirmDialogViewModel
                {
                    Title = "Confirm Replace",
                    Message = $"Replace existing pipeline \"{dialogVm.PipelineName.Trim()}\"?",
                    ConfirmText = "Replace"
                };
                var confirm = new SmartCopy.UI.Views.Workflows.ConfirmDialog { DataContext = confirmVm };
                var confirmResult = await confirm.ShowDialog<bool?>(window);
                if (confirmResult != true) return;
            }

            await _currentViewModel.SavePipelineAsync(dialogVm.PipelineName.Trim());
        }
    }

    // ---- Keyboard handling for step list ----

    private void OnStepListKeyDown(object? sender, KeyEventArgs e)
    {
        if (_currentViewModel?.SelectedStep is not { } selected) return;

        switch (e.Key)
        {
            case Key.Delete:
                _currentViewModel.RemoveStepCommand.Execute(selected);
                e.Handled = true;
                break;
            case Key.F2:
            case Key.Enter:
                _currentViewModel.RequestEditStepCommand.Execute(selected);
                e.Handled = true;
                break;
        }
    }
}
