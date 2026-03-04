using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SmartCopy.Core.Filters;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class FilterChainView : UserControl
{
    private FilterChainViewModel? _currentViewModel;

    // ---- DragDrop state ----
    private int _dragFromIndex = -1;
    private static readonly DataFormat<string> DragDataFormat =
        DataFormat.CreateStringApplicationFormat("com.smartcopy2026.filter-index");

    public FilterChainView()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (_currentViewModel != null)
            {
                _currentViewModel.Filters.CollectionChanged -= Filters_CollectionChanged;
                _currentViewModel.NewFilterDialogRequested -= OnNewFilterDialogRequested;
                _currentViewModel.EditFilterRequested -= OnEditFilterRequested;
                _currentViewModel.AddFilter.PresetPicked -= OnPresetPickedClosePopup;
                _currentViewModel.AddFilter.CloseRequested -= OnCloseRequestedClosePopup;
            }

            _currentViewModel = DataContext as FilterChainViewModel;

            if (_currentViewModel != null)
            {
                _currentViewModel.Filters.CollectionChanged += Filters_CollectionChanged;
                _currentViewModel.NewFilterDialogRequested += OnNewFilterDialogRequested;
                _currentViewModel.EditFilterRequested += OnEditFilterRequested;
                _currentViewModel.AddFilter.PresetPicked += OnPresetPickedClosePopup;
                _currentViewModel.AddFilter.CloseRequested += OnCloseRequestedClosePopup;
            }
        };

        // Wire DragDrop on the filter cards ItemsControl.
        DragDrop.SetAllowDrop(FiltersItemsControl, true);
        FiltersItemsControl.AddHandler(DragDrop.DragOverEvent, OnFiltersDragOver);
        FiltersItemsControl.AddHandler(DragDrop.DropEvent, OnFiltersDrop);
        FiltersItemsControl.ContainerPrepared += OnFilterContainerPrepared;
    }

    // ---- Add-filter popup ----

    private void OnPresetPickedClosePopup(FilterPreset _) => Dispatcher.UIThread.Post(() => AddFilterPopup.IsOpen = false);
    private void OnCloseRequestedClosePopup() => Dispatcher.UIThread.Post(() => AddFilterPopup.IsOpen = false);

    private void OnAddFilterButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        // Reset to Level 1 each time the popup opens.
        _currentViewModel?.AddFilter.GoBackCommand.Execute(null);
        AddFilterPopup.IsOpen = true;
        Dispatcher.UIThread.Post(() => AddFilterFlyoutControl.Focus(), DispatcherPriority.Loaded);
    }

    // ---- Dialog event routing ----

    private void OnNewFilterDialogRequested(object? sender, string filterType)
    {
        Dispatcher.UIThread.Post(() => AddFilterPopup.IsOpen = false);
        _ = OpenNewFilterDialogAsync(filterType);
    }

    private void OnEditFilterRequested(object? sender, FilterViewModel filterVm)
    {
        _ = OpenEditFilterDialogAsync(filterVm);
    }

    private async Task OpenNewFilterDialogAsync(string filterType)
    {
        if (_currentViewModel is null) return;
        if (this.VisualRoot is not Window parentWindow) return;

        var vm = EditFilterDialogViewModel.ForNew(
            filterType,
            _currentViewModel.PipelineDestinationPath);

        var dialog = new EditFilterDialog { DataContext = vm };
        var result = await dialog.ShowDialog<bool?>(parentWindow);

        if (result == true && vm.ResultFilter is not null)
        {
            if (vm.SaveAsPreset && !string.IsNullOrEmpty(vm.FilterName))
                await SavePresetAsync(_currentViewModel, filterType, vm.ResultFilter, vm.FilterName);

            _currentViewModel.AddFilterFromResult(vm.ResultFilter);
        }
    }

    private async Task OpenEditFilterDialogAsync(FilterViewModel filterVm)
    {
        if (_currentViewModel is null) return;
        if (this.VisualRoot is not Window parentWindow) return;

        var vm = EditFilterDialogViewModel.ForEdit(
            filterVm.BackingFilter,
            _currentViewModel.PipelineDestinationPath);

        var dialog = new EditFilterDialog { DataContext = vm };
        var result = await dialog.ShowDialog<bool?>(parentWindow);

        if (result == true && vm.ResultFilter is not null)
        {
            if (vm.SaveAsPreset && !string.IsNullOrEmpty(vm.FilterName))
                await SavePresetAsync(_currentViewModel, filterVm.BackingFilter.Config.FilterType, vm.ResultFilter, vm.FilterName);

            _currentViewModel.ReplaceFilter(filterVm, vm.ResultFilter);
        }
    }

    private static async Task SavePresetAsync(
        FilterChainViewModel chainVm,
        string filterType,
        IFilter filter,
        string name)
    {
        var preset = new FilterPreset { Name = name, Config = filter.Config };
        await chainVm.PresetStore.SaveUserPresetAsync(filterType, preset);
    }

    // ---- DragDrop: drag handle wiring ----

    private void OnFilterContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        // Attach a pointer-pressed handler to each filter card container so we can
        // detect a press on the ≡ drag handle and start a drag.
        e.Container.AddHandler(PointerPressedEvent, OnFilterCardPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    private async void OnFilterCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control container) return;

        // Walk the visual tree from the pressed element up to the container;
        // start a drag only when the press originates from the ≡ drag handle TextBlock.
        var el = e.Source as Visual;
        var isDragHandle = false;
        while (el is not null && !ReferenceEquals(el, container))
        {
            if (el is TextBlock { Text: "≡" })
            {
                isDragHandle = true;
                break;
            }
            el = el.GetVisualParent();
        }
        if (!isDragHandle) return;

        _dragFromIndex = FiltersItemsControl.IndexFromContainer(container);
        if (_dragFromIndex < 0) return;

        var item = new DataTransferItem();
        item.Set(DragDataFormat, _dragFromIndex.ToString());
        var data = new DataTransfer();
        data.Add(item);
        await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Move);
    }

    private void OnFiltersDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DragDataFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFiltersDrop(object? sender, DragEventArgs e)
    {
        if (_currentViewModel is null) return;
        if (!e.DataTransfer.Contains(DragDataFormat)) return;

        var toIndex = GetDropTargetIndex(e.GetPosition(FiltersItemsControl));
        if (toIndex >= 0 && _dragFromIndex >= 0 && toIndex != _dragFromIndex)
            _currentViewModel.MoveFilter(_dragFromIndex, toIndex);

        _dragFromIndex = -1;
    }

    private int GetDropTargetIndex(Point dropPosition)
    {
        for (var i = 0; i < FiltersItemsControl.ItemCount; i++)
        {
            var container = FiltersItemsControl.ContainerFromIndex(i);
            if (container is null) continue;
            var topLeft = container.TranslatePoint(new Point(0, 0), FiltersItemsControl);
            if (topLeft is null) continue;
            var bounds = new Rect(topLeft.Value, container.Bounds.Size);
            if (bounds.Contains(dropPosition)) return i;
        }
        return FiltersItemsControl.ItemCount > 0 ? FiltersItemsControl.ItemCount - 1 : 0;
    }

    // ---- Auto-scroll when a filter is added ----

    private void Filters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(() =>
            {
                FiltersScrollViewer?.ScrollToEnd();
            }, DispatcherPriority.Loaded);
        }
    }

    // ---- Keyboard handling for filter list ----

    private void OnFilterListKeyDown(object? sender, KeyEventArgs e)
    {
        if (_currentViewModel?.SelectedFilter is not { } selected) return;

        switch (e.Key)
        {
            case Key.Delete:
                _currentViewModel.RemoveFilterCommand.Execute(selected);
                e.Handled = true;
                break;
            case Key.F2:
            case Key.Enter:
                _currentViewModel.RequestEditFilterCommand.Execute(selected);
                e.Handled = true;
                break;
        }
    }
}
