using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using Avalonia.Controls;
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

        _mainVm = DataContext as MainViewModel;
        _workflowMenu = _mainVm?.WorkflowMenu;

        if (_workflowMenu is not null)
        {
            _workflowMenu.SavedWorkflows.CollectionChanged += OnSavedWorkflowsChanged;
            _workflowMenu.PropertyChanged += OnWorkflowMenuPropertyChanged;
        }

        RebuildWorkflowsMenu();
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
