using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform;
using SmartCopy.UI.ViewModels;
using SmartCopy.UI.ViewModels.Workflows;

namespace SmartCopy.UI.Views;

public partial class MainWindow : Window
{
    // Smallest sensible window — prevents a corrupt settings file locking the user out.
    private const double MainWindowMinWidth  = 800;
    private const double MainWindowMinHeight = 600;

    // Per-column minimum widths (pixels) for the three content columns.
    private const double MinColFilters = 150;
    private const double MinColFolders = 150;
    private const double MinColFiles   = 300;

    // Indices into ContentGrid.ColumnDefinitions for the three resizable columns
    // (0 = Filters, 2 = Folders, 4 = Files; 1 and 3 are the splitter slots).
    private const int ColIdxFilters = 0;
    private const int ColIdxFolders = 2;
    private const int ColIdxFiles   = 4;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartCopy2026",
        "window.json");

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private MainViewModel? _mainVm;
    private WorkflowMenuViewModel? _workflowMenu;
    private MenuItem? _absolutePathsMenuItem;
    private MenuItem? _autoOpenLogMenuItem;
    private MenuItem? _showExcludedNodesMenuItem;

    public MainWindow()
    {
        InitializeComponent();
        WireSourceComboBoxKeyboard();
        DataContextChanged += OnMainDataContextChanged;
    }

    private void OnMainDataContextChanged(object? sender, EventArgs e)
    {
        if (_workflowMenu is not null)
        {
            _workflowMenu.SavedWorkflows.CollectionChanged -= OnSavedWorkflowsChanged;
            _workflowMenu.PropertyChanged -= OnWorkflowMenuPropertyChanged;
        }

        if (_mainVm is not null)
            _mainVm.PropertyChanged -= OnMainViewModelPropertyChanged;

        _mainVm = DataContext as MainViewModel;
        _workflowMenu = _mainVm?.WorkflowMenu;

        if (_workflowMenu is not null)
        {
            _workflowMenu.SavedWorkflows.CollectionChanged += OnSavedWorkflowsChanged;
            _workflowMenu.PropertyChanged += OnWorkflowMenuPropertyChanged;
        }

        if (_mainVm is not null)
            _mainVm.PropertyChanged += OnMainViewModelPropertyChanged;

        RebuildWorkflowsMenu();
        BuildSelectionMenu();
        BuildOptionsMenu();
    }

    private void OnSavedWorkflowsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildWorkflowsMenu();

    private void OnWorkflowMenuPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkflowMenuViewModel.CanSave))
        {
            if (WorkflowsMenu.Items is { Count: > 0 } && WorkflowsMenu.Items[0] is MenuItem saveItem)
            {
                saveItem.IsEnabled = _workflowMenu?.CanSave ?? false;
            }
        }
    }

    private void RebuildWorkflowsMenu()
    {
        WorkflowsMenu.Items.Clear();

        var saveItem = new MenuItem
        {
            Header = "_Save Workflow...",
            IsEnabled = _workflowMenu?.CanSave ?? false,
        };
        saveItem.Click += (_, _) => _workflowMenu?.SaveWorkflowCommand.Execute(null);
        WorkflowsMenu.Items.Add(saveItem);

        var manageItem = new MenuItem { Header = "_Manage Workflows..." };
        manageItem.Click += (_, _) => _workflowMenu?.ManageWorkflowsCommand.Execute(null);
        WorkflowsMenu.Items.Add(manageItem);

        if (_workflowMenu?.SavedWorkflows.Count > 0)
        {
            WorkflowsMenu.Items.Add(new Separator());
            foreach (var preset in _workflowMenu.SavedWorkflows)
            {
                var name = preset.Name;
                var item = new MenuItem { Header = name };
                item.Click += (_, _) => _workflowMenu?.LoadWorkflowCommand.Execute(name);
                WorkflowsMenu.Items.Add(item);
            }
        }
    }

    private void BuildSelectionMenu()
    {
        SelectionMenu.Items.Clear();

        Add(item("Select _All",       new KeyGesture(Key.A, KeyModifiers.Control)),
            () => _mainVm?.SelectAllCommand.Execute(null));
        Add(item("_Invert Selection", new KeyGesture(Key.I, KeyModifiers.Control)),
            () => _mainVm?.InvertSelectionCommand.Execute(null));
        Add(item("_Clear Selection",  new KeyGesture(Key.A, KeyModifiers.Control | KeyModifiers.Shift)),
            () => _mainVm?.ClearSelectionCommand.Execute(null));

        SelectionMenu.Items.Add(new Separator());

        Add(item("Save as _Text..."),      () => _mainVm?.SaveSelectionAsTextCommand.Execute(null));
        Add(item("Save as _Playlist..."),  () => _mainVm?.SaveSelectionAsPlaylistCommand.Execute(null));
        Add(item("_Restore From File..."), () => _mainVm?.RestoreSelectionCommand.Execute(null));

        SelectionMenu.Items.Add(new Separator());

        _absolutePathsMenuItem = new MenuItem
        {
            Header = "Save With _Absolute Paths",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _mainVm?.UseAbsolutePathsForSelection ?? false,
        };
        _absolutePathsMenuItem.Click += (_, _) =>
        {
            if (_mainVm is not null)
                _mainVm.UseAbsolutePathsForSelection = !_mainVm.UseAbsolutePathsForSelection;
        };
        SelectionMenu.Items.Add(_absolutePathsMenuItem);

        return;

        static MenuItem item(string header, KeyGesture? gesture = null)
            => new() { Header = header, InputGesture = gesture };

        void Add(MenuItem m, Action onClick)
        {
            m.Click += (_, _) => onClick();
            SelectionMenu.Items.Add(m);
        }
    }

    private void BuildOptionsMenu()
    {
        OptionsMenu.Items.Clear();

        _autoOpenLogMenuItem = new MenuItem
        {
            Header = "Auto-open _Log on Run",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _mainVm?.AutoOpenLogOnRun ?? true,
        };
        _autoOpenLogMenuItem.Click += (_, _) =>
        {
            if (_mainVm is not null)
                _mainVm.AutoOpenLogOnRun = !_mainVm.AutoOpenLogOnRun;
        };
        OptionsMenu.Items.Add(_autoOpenLogMenuItem);

        _showExcludedNodesMenuItem = new MenuItem
        {
            Header = "Show _Excluded Nodes in Tree",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _mainVm?.ShowExcludedNodesByDefault ?? true,
        };
        _showExcludedNodesMenuItem.Click += (_, _) =>
        {
            if (_mainVm is not null)
                _mainVm.ShowExcludedNodesByDefault = !_mainVm.ShowExcludedNodesByDefault;
        };
        OptionsMenu.Items.Add(_showExcludedNodesMenuItem);
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.UseAbsolutePathsForSelection)
            && _absolutePathsMenuItem is not null)
        {
            _absolutePathsMenuItem.IsChecked = _mainVm?.UseAbsolutePathsForSelection ?? false;
        }

        if (e.PropertyName == nameof(MainViewModel.AutoOpenLogOnRun)
            && _autoOpenLogMenuItem is not null)
        {
            _autoOpenLogMenuItem.IsChecked = _mainVm?.AutoOpenLogOnRun ?? true;
        }

        if (e.PropertyName == nameof(MainViewModel.ShowExcludedNodesByDefault)
            && _showExcludedNodesMenuItem is not null)
        {
            _showExcludedNodesMenuItem.IsChecked = _mainVm?.ShowExcludedNodesByDefault ?? true;
        }
    }

    // ── Restore ────────────────────────────────────────────────────────────────

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        TryRestoreWindowState();
    }

    private void TryRestoreWindowState()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return;

            var json = File.ReadAllText(SettingsPath);
            var s    = JsonSerializer.Deserialize<WindowSettings>(json);
            if (s is null)
                return;

            // Clamp size to the minimum floor.
            var w = Math.Max(s.Width,  MainWindowMinWidth);
            var h = Math.Max(s.Height, MainWindowMinHeight);

            // Only restore the position when it's safe — i.e. the top-left corner
            // (plus a 100 px margin) lands on at least one physical screen.
            if (s.X.HasValue && s.Y.HasValue && IsPositionOnScreen(s.X.Value, s.Y.Value))
            {
                Position = new Avalonia.PixelPoint((int)s.X.Value, (int)s.Y.Value);
                WindowStartupLocation = WindowStartupLocation.Manual;
            }

            Width  = w;
            Height = h;

            if (s.IsMaximized)
                WindowState = WindowState.Maximized;

            // Restore column widths as absolute pixel values, clamped to each
            // column's minimum so a corrupt file can't produce unusable columns.
            var cols = ContentGrid.ColumnDefinitions;
            if (s.ColWidthFilters.HasValue)
                cols[ColIdxFilters].Width = new GridLength(
                    Math.Max(s.ColWidthFilters.Value, MinColFilters), GridUnitType.Pixel);

            if (s.ColWidthFolders.HasValue)
                cols[ColIdxFolders].Width = new GridLength(
                    Math.Max(s.ColWidthFolders.Value, MinColFolders), GridUnitType.Pixel);

        }
        catch
        {
            // Corrupt / unreadable settings — silently fall back to defaults.
        }
    }

    /// <summary>
    /// Returns true if the point (x, y) — offset inward by 100 px so at least a
    /// sliver of the title-bar is grabbable — falls within any connected screen.
    /// </summary>
    private bool IsPositionOnScreen(double x, double y)
    {
        var screens = Screens.All;
        if (screens is null || screens.Count == 0)
            return false;

        const int margin = 100;
        foreach (var screen in screens)
        {
            var b = screen.Bounds;
            if (x + margin >= b.X              &&
                y + margin >= b.Y              &&
                x          <  b.X + b.Width   &&
                y          <  b.Y + b.Height)
                return true;
        }
        return false;
    }

    // ── Save ───────────────────────────────────────────────────────────────────

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        TrySaveWindowState();
    }

    private void TrySaveWindowState()
    {
        try
        {
            var isMaximized = WindowState == WindowState.Maximized;

            // Read the actual rendered pixel widths of the three content columns.
            var cols = ContentGrid.ColumnDefinitions;

            var s = new WindowSettings
            {
                // Store the restored (non-maximised) bounds so the window comes back
                // at a sensible size when the user un-maximises it next time.
                Width           = isMaximized ? Width  : ClientSize.Width,
                Height          = isMaximized ? Height : ClientSize.Height,
                X               = Position.X,
                Y               = Position.Y,
                IsMaximized     = isMaximized,
                ColWidthFilters = cols[ColIdxFilters].ActualWidth,
                ColWidthFolders = cols[ColIdxFolders].ActualWidth,
            };

            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, s_jsonOptions));
        }
        catch
        {
            // Best-effort — never crash on close just because we couldn't save prefs.
        }
    }

    // ── Source ComboBox UX ──────────────────────────────────────────────────────
    // Keyboard: Tunnel handler fires BEFORE the ComboBox's built-in key handler.
    // Mouse: SelectionChanged while dropdown is open sets _applyOnDropDownClose;
    //        DropDownClosed checks it and applies if set.

    private bool _applyOnDropDownClose;

    private void WireSourceComboBoxKeyboard()
    {
        SourceComboBox.AddHandler(
            KeyDownEvent,
            OnSourceComboBoxKeyDown,
            Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // If the selection changes while the dropdown is open, the user picked an item.
        SourceComboBox.SelectionChanged += (_, _) =>
        {
            if (SourceComboBox.IsDropDownOpen)
                _applyOnDropDownClose = true;
        };

        SourceComboBox.DropDownClosed += OnSourceComboBoxDropDownClosed;
    }

    private void OnSourceComboBoxDropDownClosed(object? sender, EventArgs e)
    {
        if (!_applyOnDropDownClose) return;
        _applyOnDropDownClose = false;

        // Defer so the SelectedItem → SourcePath binding settles
        // after the popup disposes.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is MainViewModel vm && !string.IsNullOrWhiteSpace(vm.SourcePath))
                vm.ApplySourcePathCommand.Execute(null);
        });
    }

    private void OnSourceComboBoxKeyDown(object? sender, KeyEventArgs e)
    {
        var combo = SourceComboBox;

        switch (e.Key)
        {
            // Enter → commit the current text/selection and apply.
            case Key.Enter:
                combo.IsDropDownOpen = false;
                if (DataContext is MainViewModel vm)
                {
                    vm.ApplySourcePathCommand.Execute(null);
                }
                e.Handled = true;
                break;

            // Escape → close dropdown if open, otherwise revert path.
            case Key.Escape:
                if (combo.IsDropDownOpen)
                {
                    combo.IsDropDownOpen = false;
                }

                if (DataContext is MainViewModel rv)
                {
                    rv.RevertSourcePathCommand.Execute(null);
                }
                e.Handled = true;
                break;
        }
    }

    // ── Settings record ────────────────────────────────────────────────────────

    private sealed class WindowSettings
    {
        public double  Width           { get; set; } = 1400;
        public double  Height          { get; set; } = 860;
        public double? X               { get; set; }
        public double? Y               { get; set; }
        public bool    IsMaximized     { get; set; }
        public double? ColWidthFilters { get; set; }
        public double? ColWidthFolders { get; set; }
    }
}
