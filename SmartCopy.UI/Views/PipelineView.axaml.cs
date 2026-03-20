using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SmartCopy.Core.Pipeline;
using SmartCopy.UI.ViewModels;
using SmartCopy.UI.ViewModels.Dialogs;
using SmartCopy.UI.ViewModels.Pipeline;
using SmartCopy.UI.Views.Dialogs;
using SmartCopy.UI.Views.Pipeline;

namespace SmartCopy.UI.Views;

public partial class PipelineView : UserControl
{
    private PipelineViewModel? _currentViewModel;

    // ---- DragDrop state ----
    private int _dragFromIndex = -1;
    private static readonly DataFormat<string> DragDataFormat =
        DataFormat.CreateStringApplicationFormat("com.smartcopy2026.pipeline-step-index");

    public PipelineView()
    {
        InitializeComponent();

        DragDrop.SetAllowDrop(StepsItemsControl, true);
        StepsItemsControl.AddHandler(DragDrop.DragOverEvent, OnStepsDragOver);
        StepsItemsControl.AddHandler(DragDrop.DropEvent, OnStepsDrop);
        StepsItemsControl.ContainerPrepared += OnStepContainerPrepared;

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
        if (_currentViewModel?.IsRunning == true) return;
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

        if (await _currentViewModel.TryAddStepWithoutConfiguration(kind))
            return;

        if (this.VisualRoot is not Window parentWindow)
            return;

        var vm = EditStepDialogViewModel.ForNew(kind, _currentViewModel.AppContext, _currentViewModel.SourceCapabilities);
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

            await _currentViewModel.AddStepFromResult(vm.ResultStep, vm.ResultCustomName);
        }
    }

    private async void OnEditStepRequested(object? sender, PipelineStepViewModel step)
    {
        if (_currentViewModel is null || this.VisualRoot is not Window parentWindow)
            return;

        if (!step.Step.IsEditable)
            return;

        var vm = EditStepDialogViewModel.ForEdit(step, _currentViewModel.AppContext, _currentViewModel.SourceCapabilities);
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

            await _currentViewModel.ReplaceStep(step, vm.ResultStep, vm.ResultCustomName);
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

    // ---- DragDrop: drag handle wiring ----

    private void OnStepContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        e.Container.AddHandler(PointerPressedEvent, OnStepCardPointerPressed, RoutingStrategies.Tunnel);
    }

    private async void OnStepCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control container) return;

        var el = e.Source as Visual;
        var isDragHandle = false;
        while (el is not null && !ReferenceEquals(el, container))
        {
            if (el is Control { Name: "DragHandle" })
            {
                isDragHandle = true;
                break;
            }

            el = el.GetVisualParent();
        }
        if (!isDragHandle) return;

        _dragFromIndex = StepsItemsControl.IndexFromContainer(container);
        if (_dragFromIndex < 0) return;

        var item = new DataTransferItem();
        item.Set(DragDataFormat, _dragFromIndex.ToString());
        var data = new DataTransfer();
        data.Add(item);
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private void OnStepsDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DragDataFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnStepsDrop(object? sender, DragEventArgs e)
    {
        if (_currentViewModel is null) return;
        if (_currentViewModel.IsRunning) return;
        if (!e.DataTransfer.Contains(DragDataFormat)) return;

        var toIndex = GetDropTargetIndex(e.GetPosition(StepsItemsControl));
        if (toIndex >= 0 && _dragFromIndex >= 0 && toIndex != _dragFromIndex)
            _currentViewModel.MoveStep(_dragFromIndex, toIndex);

        _dragFromIndex = -1;
    }

    private int GetDropTargetIndex(Point dropPosition)
    {
        for (var i = 0; i < StepsItemsControl.ItemCount; i++)
        {
            var container = StepsItemsControl.ContainerFromIndex(i);
            if (container is null) continue;
            var topLeft = container.TranslatePoint(new Point(0, 0), StepsItemsControl);
            if (topLeft is null) continue;
            var bounds = new Rect(topLeft.Value, container.Bounds.Size);
            if (bounds.Contains(dropPosition)) return i;
        }
        return StepsItemsControl.ItemCount > 0 ? StepsItemsControl.ItemCount - 1 : 0;
    }

    // ---- Keyboard handling for step list ----

    private void OnStepContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is StackPanel { DataContext: PipelineStepViewModel step } && !step.HasDestination)
            e.Handled = true;
    }

    private void OnSwapWithSourceClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: PipelineStepViewModel step }) return;
        _currentViewModel?.RequestSwapWithSource(step);
        e.Handled = true;
    }

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
