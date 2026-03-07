using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
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

    // Indices into ContentGrid.ColumnDefinitions for the three resizable columns
    // (0 = Filters, 2 = Folders, 4 = Files; 1 and 3 are the splitter slots).
    private const int ColIdxFilters = 0;
    private const int ColIdxFolders = 2;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartCopy2026",
        "window.json");

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private MainViewModel? _mainVm;
    private WorkflowMenuViewModel? _workflowMenu;

    // Selection menu
    private MenuItem? _absolutePathsMenuItem;

    // Options menu — Display
    private MenuItem? _autoOpenLogMenuItem;
    private MenuItem? _showExcludedNodesMenuItem;

    // Options menu — Startup
    private MenuItem? _restoreLastWorkflowMenuItem;
    private MenuItem? _restoreLastSourcePathMenuItem;
    private MenuItem? _saveSessionLocallyMenuItem;

    // Options menu — Pipeline
    private MenuItem? _disableDestructivePreviewMenuItem;
    private MenuItem? _deleteToRecycleBinMenuItem;
    private MenuItem? _overwriteSkipMenuItem;
    private MenuItem? _overwriteAlwaysMenuItem;
    private MenuItem? _overwriteIfNewerMenuItem;

    // Options menu — Scan
    private MenuItem? _fullPreScanMenuItem;
    private MenuItem? _lazyExpandScanMenuItem;
    private MenuItem? _followSymlinksMenuItem;

    // Options menu — Debug
    private MenuItem? _artificialDelayMenuItem;

    public MainWindow()
    {
        InitializeComponent();

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

        // ── Section: Startup ───────────────────────────────────────────────────
        OptionsMenu.Items.Add(SectionHeader("Startup"));

        _restoreLastWorkflowMenuItem = Toggle(
            "Restore Last _Workflow on Startup",
            _mainVm?.RestoreLastWorkflow ?? false,
            () => { if (_mainVm is not null) _mainVm.RestoreLastWorkflow = !_mainVm.RestoreLastWorkflow; });
        OptionsMenu.Items.Add(_restoreLastWorkflowMenuItem);

        _saveSessionLocallyMenuItem = Toggle(
            "Save Session _Locally (portable)",
            _mainVm?.SaveSessionLocally ?? false,
            () => { if (_mainVm is not null) _mainVm.SaveSessionLocally = !_mainVm.SaveSessionLocally; });
        // Only meaningful when Restore Last Workflow is on.
        _saveSessionLocallyMenuItem.IsEnabled = _mainVm?.RestoreLastWorkflow ?? false;
        OptionsMenu.Items.Add(_saveSessionLocallyMenuItem);

        _restoreLastSourcePathMenuItem = Toggle(
            "Restore Last _Source Path on Startup",
            _mainVm?.RestoreLastSourcePath ?? true,
            () => { if (_mainVm is not null) _mainVm.RestoreLastSourcePath = !_mainVm.RestoreLastSourcePath; });
        // Disable when Restore Workflow is on (redundant)
        _restoreLastSourcePathMenuItem.IsEnabled = !(_mainVm?.RestoreLastWorkflow ?? false);
        OptionsMenu.Items.Add(_restoreLastSourcePathMenuItem);

        // ── Section: Display ───────────────────────────────────────────────────
        OptionsMenu.Items.Add(new Separator());
        OptionsMenu.Items.Add(SectionHeader("Display"));

        _showExcludedNodesMenuItem = Toggle(
            "Show _Excluded Nodes in Tree",
            _mainVm?.ShowExcludedNodesByDefault ?? true,
            () => { if (_mainVm is not null) _mainVm.ShowExcludedNodesByDefault = !_mainVm.ShowExcludedNodesByDefault; });
        OptionsMenu.Items.Add(_showExcludedNodesMenuItem);

        _autoOpenLogMenuItem = Toggle(
            "Auto-open _Log on Run",
            _mainVm?.AutoOpenLogOnRun ?? true,
            () => { if (_mainVm is not null) _mainVm.AutoOpenLogOnRun = !_mainVm.AutoOpenLogOnRun; });
        OptionsMenu.Items.Add(_autoOpenLogMenuItem);

        // ── Section: Pipeline ──────────────────────────────────────────────────
        OptionsMenu.Items.Add(new Separator());
        OptionsMenu.Items.Add(SectionHeader("Pipeline"));

        _disableDestructivePreviewMenuItem = Toggle(
            "_Skip Destructive Preview",
            _mainVm?.DisableDestructivePreview ?? false,
            () => { if (_mainVm is not null) _mainVm.DisableDestructivePreview = !_mainVm.DisableDestructivePreview; });
        OptionsMenu.Items.Add(_disableDestructivePreviewMenuItem);

        _deleteToRecycleBinMenuItem = Toggle(
            "_Delete to Recycle Bin",
            _mainVm?.DeleteToRecycleBin ?? true,
            () => { if (_mainVm is not null) _mainVm.DeleteToRecycleBin = !_mainVm.DeleteToRecycleBin; });
        OptionsMenu.Items.Add(_deleteToRecycleBinMenuItem);

        // Overwrite mode submenu
        var currentMode = _mainVm?.DefaultOverwriteMode ?? "Skip";
        _overwriteSkipMenuItem   = OverwriteRadio("_Skip (keep existing)",    "Skip",     currentMode);
        _overwriteAlwaysMenuItem  = OverwriteRadio("_Always Overwrite",        "Always",   currentMode);
        _overwriteIfNewerMenuItem = OverwriteRadio("Overwrite _if Newer",      "IfNewer",  currentMode);

        var overwriteSubMenu = new MenuItem { Header = "Default _Overwrite Mode" };
        overwriteSubMenu.Items.Add(_overwriteSkipMenuItem);
        overwriteSubMenu.Items.Add(_overwriteAlwaysMenuItem);
        overwriteSubMenu.Items.Add(_overwriteIfNewerMenuItem);
        OptionsMenu.Items.Add(overwriteSubMenu);

        // ── Section: Scan ─────────────────────────────────────────────────────
        OptionsMenu.Items.Add(new Separator());
        OptionsMenu.Items.Add(SectionHeader("Scan"));

        _fullPreScanMenuItem = Toggle(
            "_Full Pre-Scan",
            _mainVm?.FullPreScan ?? false,
            () => { if (_mainVm is not null) _mainVm.FullPreScan = !_mainVm.FullPreScan; });
        OptionsMenu.Items.Add(_fullPreScanMenuItem);

        _lazyExpandScanMenuItem = Toggle(
            "_Lazy Scan (scan on demand)",
            _mainVm?.LazyExpandScan ?? false,
            () => { if (_mainVm is not null) _mainVm.LazyExpandScan = !_mainVm.LazyExpandScan; });
        OptionsMenu.Items.Add(_lazyExpandScanMenuItem);

        _followSymlinksMenuItem = Toggle(
            "_Follow Symlinks",
            _mainVm?.FollowSymlinks ?? false,
            () => { if (_mainVm is not null) _mainVm.FollowSymlinks = !_mainVm.FollowSymlinks; });
        OptionsMenu.Items.Add(_followSymlinksMenuItem);

        // ── Section: Debug  ───────────────────────────────────────────────────
        OptionsMenu.Items.Add(new Separator());
        OptionsMenu.Items.Add(SectionHeader("Debug"));

        _artificialDelayMenuItem = new MenuItem
        {
            Header = "Add Artificial Delays to Mock Provider",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _mainVm?.AddArtificialDelay ?? false,
        };
        OptionsMenu.Items.Add(_artificialDelayMenuItem);

        return;

        static MenuItem SectionHeader(string text)
        {
            var header = new MenuItem
            {
                Header = text,
                IsEnabled = false,
                FontWeight = Avalonia.Media.FontWeight.Bold,
                Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#a0a0a0")),
            };
            return header;
        }

        static MenuItem Toggle(string header, bool isChecked, Action onClick)
        {
            var item = new MenuItem
            {
                Header = header,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = isChecked,
            };
            item.Click += (_, _) => onClick();
            return item;
        }

        MenuItem OverwriteRadio(string header, string mode, string currentMode)
        {
            var item = new MenuItem
            {
                Header = header,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = string.Equals(currentMode, mode, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) =>
            {
                if (_mainVm is null) return;
                _mainVm.DefaultOverwriteMode = mode;
            };
            return item;
        }
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.UseAbsolutePathsForSelection):
                if (_absolutePathsMenuItem is not null)
                    _absolutePathsMenuItem.IsChecked = _mainVm?.UseAbsolutePathsForSelection ?? false;
                break;

            case nameof(MainViewModel.AutoOpenLogOnRun):
                if (_autoOpenLogMenuItem is not null)
                    _autoOpenLogMenuItem.IsChecked = _mainVm?.AutoOpenLogOnRun ?? true;
                break;

            case nameof(MainViewModel.ShowExcludedNodesByDefault):
                if (_showExcludedNodesMenuItem is not null)
                    _showExcludedNodesMenuItem.IsChecked = _mainVm?.ShowExcludedNodesByDefault ?? true;
                break;

            case nameof(MainViewModel.RestoreLastWorkflow):
                if (_restoreLastWorkflowMenuItem is not null)
                    _restoreLastWorkflowMenuItem.IsChecked = _mainVm?.RestoreLastWorkflow ?? false;
                // "Restore source path" is redundant when workflow restore is active.
                if (_restoreLastSourcePathMenuItem is not null)
                    _restoreLastSourcePathMenuItem.IsEnabled = !(_mainVm?.RestoreLastWorkflow ?? false);
                // "Save session locally" only makes sense when workflow restore is active.
                if (_saveSessionLocallyMenuItem is not null)
                    _saveSessionLocallyMenuItem.IsEnabled = _mainVm?.RestoreLastWorkflow ?? false;
                break;

            case nameof(MainViewModel.RestoreLastSourcePath):
                if (_restoreLastSourcePathMenuItem is not null)
                    _restoreLastSourcePathMenuItem.IsChecked = _mainVm?.RestoreLastSourcePath ?? true;
                break;

            case nameof(MainViewModel.SaveSessionLocally):
                if (_saveSessionLocallyMenuItem is not null)
                    _saveSessionLocallyMenuItem.IsChecked = _mainVm?.SaveSessionLocally ?? false;
                break;

            case nameof(MainViewModel.DisableDestructivePreview):
                if (_disableDestructivePreviewMenuItem is not null)
                    _disableDestructivePreviewMenuItem.IsChecked = _mainVm?.DisableDestructivePreview ?? false;
                break;

            case nameof(MainViewModel.DeleteToRecycleBin):
                if (_deleteToRecycleBinMenuItem is not null)
                    _deleteToRecycleBinMenuItem.IsChecked = _mainVm?.DeleteToRecycleBin ?? true;
                break;

            case nameof(MainViewModel.DefaultOverwriteMode):
                var mode = _mainVm?.DefaultOverwriteMode ?? "Skip";
                if (_overwriteSkipMenuItem    is not null) _overwriteSkipMenuItem.IsChecked    = mode == "Skip";
                if (_overwriteAlwaysMenuItem  is not null) _overwriteAlwaysMenuItem.IsChecked  = mode == "Always";
                if (_overwriteIfNewerMenuItem is not null) _overwriteIfNewerMenuItem.IsChecked = mode == "IfNewer";
                break;

            case nameof(MainViewModel.FullPreScan):
                if (_fullPreScanMenuItem is not null)
                    _fullPreScanMenuItem.IsChecked = _mainVm?.FullPreScan ?? false;
                break;

            case nameof(MainViewModel.LazyExpandScan):
                if (_lazyExpandScanMenuItem is not null)
                    _lazyExpandScanMenuItem.IsChecked = _mainVm?.LazyExpandScan ?? false;
                break;

            case nameof(MainViewModel.FollowSymlinks):
                if (_followSymlinksMenuItem is not null)
                    _followSymlinksMenuItem.IsChecked = _mainVm?.FollowSymlinks ?? false;
                break;
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
        // Best-effort session snapshot — allows RestoreLastWorkflow to capture
        // the exact state at exit, even if the user never explicitly saved.
        if (_mainVm?.RestoreLastWorkflow == true)
        {
            _ = _mainVm.SaveSessionSnapshotAsync();
        }
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
