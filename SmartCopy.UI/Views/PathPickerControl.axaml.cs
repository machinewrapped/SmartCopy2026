using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class PathPickerControl : UserControl
{
    public static readonly StyledProperty<string> BrowseDialogTitleProperty =
        AvaloniaProperty.Register<PathPickerControl, string>(nameof(BrowseDialogTitle), "Select folder");

    public string BrowseDialogTitle
    {
        get => GetValue(BrowseDialogTitleProperty);
        set => SetValue(BrowseDialogTitleProperty, value);
    }

    public static readonly StyledProperty<string> BrowseButtonToolTipProperty =
        AvaloniaProperty.Register<PathPickerControl, string>(nameof(BrowseButtonToolTip), "Browse for folder");

    public string BrowseButtonToolTip
    {
        get => GetValue(BrowseButtonToolTipProperty);
        set => SetValue(BrowseButtonToolTipProperty, value);
    }

    private bool _applyOnDropDownClose;

    public PathPickerControl()
    {
        InitializeComponent();
        WireComboBoxKeyboard();
    }

    private void WireComboBoxKeyboard()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        PathComboBox.AddHandler(
            KeyDownEvent,
            OnComboBoxKeyDown,
            Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // If the selection changes while the dropdown is open, the user picked an item.
        PathComboBox.SelectionChanged += (_, _) =>
        {
            if (PathComboBox.IsDropDownOpen)
                _applyOnDropDownClose = true;
        };

        PathComboBox.DropDownClosed += OnComboBoxDropDownClosed;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not TopLevel topLevel)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = BrowseDialogTitle,
            AllowMultiple = false,
        });

        if (folders is not { Count: > 0 })
            return;

        var selectedUri = folders[0].Path;
        if (!selectedUri.IsAbsoluteUri || !selectedUri.IsFile)
            return;

        if (DataContext is PathPickerViewModel vm)
        {
            vm.Path = selectedUri.LocalPath;
            vm.ApplyPathCommand.Execute(null);
        }
    }

    private void OnComboBoxDropDownClosed(object? sender, EventArgs e)
    {
        if (!_applyOnDropDownClose) return;
        _applyOnDropDownClose = false;

        // Defer so the SelectedItem → Path binding settles after the popup disposes.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is PathPickerViewModel vm && !string.IsNullOrWhiteSpace(vm.Path))
            {
                vm.ApplyPathCommand.Execute(null);
            }
        });
    }

    private void OnComboBoxKeyDown(object? sender, KeyEventArgs e)
    {
        var combo = PathComboBox;

        switch (e.Key)
        {
            // Enter → commit the current text/selection and apply.
            case Key.Enter:
                combo.IsDropDownOpen = false;
                if (DataContext is PathPickerViewModel vm)
                {
                    vm.ApplyPathCommand.Execute(null);
                }
                e.Handled = true;
                break;

            // Escape → close dropdown if open, otherwise revert path.
            case Key.Escape:
                if (combo.IsDropDownOpen)
                {
                    combo.IsDropDownOpen = false;
                }

                if (DataContext is PathPickerViewModel rv)
                {
                    rv.RevertPathCommand.Execute(null);
                }
                e.Handled = true;
                break;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        var files = e.Data.GetFiles();
#pragma warning restore CS0618
        
        // Only accept exactly one item that is a directory
        if (files != null)
        {
            var filesList = files as IReadOnlyList<IStorageItem> ?? Enumerable.ToList(files);
            if (filesList.Count == 1 && filesList[0] is IStorageFolder folder)
            {
                if (folder.TryGetLocalPath() != null || folder.Path.IsAbsoluteUri)
                {
                    e.DragEffects = e.DragEffects & (DragDropEffects.Copy | DragDropEffects.Move);
                    e.Handled = true;
                    return;
                }
            }
        }
        
        e.DragEffects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
#pragma warning disable CS0618
        var files = e.Data.GetFiles();
#pragma warning restore CS0618
        
        if (files != null)
        {
            var filesList = files as IReadOnlyList<IStorageItem> ?? Enumerable.ToList(files);
            if (filesList.Count == 1 && filesList[0] is IStorageFolder folder)
            {
                var path = folder.TryGetLocalPath() ?? folder.Path.LocalPath;
                if (!string.IsNullOrWhiteSpace(path) && DataContext is PathPickerViewModel vm)
                {
                    vm.Path = path;
                    vm.ApplyPathCommand.Execute(null);
                }
            }
        }
        
        e.Handled = true;
    }
}
