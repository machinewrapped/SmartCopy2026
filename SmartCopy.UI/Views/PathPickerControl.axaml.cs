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

    // ── Source ComboBox UX ──────────────────────────────────────────────────────
    // Keyboard: Tunnel handler fires BEFORE the ComboBox's built-in key handler.
    // Mouse: SelectionChanged while dropdown is open sets _applyOnDropDownClose;
    //        DropDownClosed checks it and applies if set.
    private bool _applyOnDropDownClose;

    public PathPickerControl()
    {
        InitializeComponent();
        WireComboBoxKeyboard();
    }

    private void WireComboBoxKeyboard()
    {
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        PathComboBox.AddHandler(
            KeyDownEvent,
            OnComboBoxKeyDown,
            RoutingStrategies.Tunnel);

        // If the selection changes while the dropdown is open, the user picked an item.
        PathComboBox.SelectionChanged += (_, _) =>
        {
            if (PathComboBox.IsDropDownOpen)
                _applyOnDropDownClose = true;
        };

        PathComboBox.DropDownClosed += OnComboBoxDropDownClosed;
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
        // Defer detailed validation to the drop handler. Only check for presence of files or folders.
        if (e.DataTransfer.Items.Any(x => x.Formats.Contains(DataFormat.File)))
        {
            e.DragEffects &= DragDropEffects.Copy | DragDropEffects.Move;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var items = e.DataTransfer.Items;
        if (items is null)
        {
            e.Handled = true;
            return;
        }

        foreach (IDataTransferItem item in items)
        {
            if (!item.Formats.Contains(DataFormat.File)) 
                continue;

            if (item.TryGetFile() is IStorageFolder folder)
            {
                var path = folder.TryGetLocalPath();

                if (!string.IsNullOrWhiteSpace(path) && DataContext is PathPickerViewModel vm)
                {
                    vm.Path = path;
                    vm.ApplyPathCommand.Execute(null);
                }
            }

            break;
        }
        
        e.Handled = true;
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
}
