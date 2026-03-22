using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class FilePathPickerControl : UserControl
{
    public static readonly StyledProperty<string> BrowseDialogTitleProperty =
        AvaloniaProperty.Register<FilePathPickerControl, string>(nameof(BrowseDialogTitle), "Browse");

    public string BrowseDialogTitle
    {
        get => GetValue(BrowseDialogTitleProperty);
        set => SetValue(BrowseDialogTitleProperty, value);
    }

    public static readonly StyledProperty<string> BrowseButtonToolTipProperty =
        AvaloniaProperty.Register<FilePathPickerControl, string>(nameof(BrowseButtonToolTip), "Browse for file");

    public string BrowseButtonToolTip
    {
        get => GetValue(BrowseButtonToolTipProperty);
        set => SetValue(BrowseButtonToolTipProperty, value);
    }

    public static readonly StyledProperty<bool> IsFileSaveProperty =
        AvaloniaProperty.Register<FilePathPickerControl, bool>(nameof(IsFileSave), false);

    public bool IsFileSave
    {
        get => GetValue(IsFileSaveProperty);
        set => SetValue(IsFileSaveProperty, value);
    }

    /// <summary>File type filters shown in the dialog. Set from code-behind after InitializeComponent.</summary>
    public IReadOnlyList<FilePickerFileType>? FileTypeChoices { get; set; }

    /// <summary>Suggested file name for Save dialogs.</summary>
    public string? SuggestedFileName { get; set; }

    /// <summary>Default extension for Save dialogs (e.g. ".sc2sel").</summary>
    public string? DefaultExtension { get; set; }

    private bool _applyOnDropDownClose;

    public FilePathPickerControl()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        PathComboBox.AddHandler(KeyDownEvent, OnComboBoxKeyDown, RoutingStrategies.Tunnel);

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

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is PathPickerViewModel vm && !string.IsNullOrWhiteSpace(vm.Path))
                vm.ApplyPathCommand.Execute(null);
        });
    }

    private void OnComboBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                PathComboBox.IsDropDownOpen = false;
                if (DataContext is PathPickerViewModel vm)
                    vm.ApplyPathCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                if (PathComboBox.IsDropDownOpen)
                    PathComboBox.IsDropDownOpen = false;
                if (DataContext is PathPickerViewModel rv)
                    rv.RevertPathCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Items.Any(x => x.Formats.Contains(DataFormat.File)))
            e.DragEffects &= DragDropEffects.Copy | DragDropEffects.Move;
        else
            e.DragEffects = DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        var items = e.DataTransfer.Items;
        if (items is null) { e.Handled = true; return; }

        foreach (IDataTransferItem item in items)
        {
            if (!item.Formats.Contains(DataFormat.File)) continue;

            // Accept dropped files (not folders)
            if (item.TryGetFile() is IStorageFile file)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(path) && DataContext is PathPickerViewModel vm)
                {
                    vm.Path = path;
                    vm.ApplyPathCommand.Execute(null);
                }
                break;
            }
        }

        e.Handled = true;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not TopLevel topLevel) return;

        if (IsFileSave)
        {
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = BrowseDialogTitle,
                SuggestedFileName = SuggestedFileName,
                DefaultExtension = DefaultExtension,
                FileTypeChoices = FileTypeChoices,
            });

            if (file is not null && DataContext is PathPickerViewModel vm)
            {
                vm.Path = file.Path.LocalPath;
                vm.ApplyPathCommand.Execute(null);
            }
        }
        else
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = BrowseDialogTitle,
                AllowMultiple = false,
                FileTypeFilter = FileTypeChoices,
            });

            if (files is { Count: > 0 } && DataContext is PathPickerViewModel vm)
            {
                vm.Path = files[0].Path.LocalPath;
                vm.ApplyPathCommand.Execute(null);
            }
        }
    }
}
