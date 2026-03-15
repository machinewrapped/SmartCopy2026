using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class LogPanelView : UserControl
{
    private LogPanelViewModel? _vm;

    public LogPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        LogListBox.AddHandler(KeyDownEvent, OnLogKeyDown, RoutingStrategies.Tunnel);
        CopyButton.Click += (_, _) => _ = CopyToClipboardAsync(selectedOnly: false);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.DisplayedEntries.CollectionChanged -= OnDisplayedEntriesChanged;

        _vm = DataContext as LogPanelViewModel;

        if (_vm is not null)
            _vm.DisplayedEntries.CollectionChanged += OnDisplayedEntriesChanged;
    }

    private void OnDisplayedEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || _vm is null) return;

        var sv = LogListBox.FindDescendantOfType<ScrollViewer>();
        bool wasAtBottom = sv == null || sv.Extent.Height - sv.Offset.Y <= sv.Viewport.Height + 2.0;

        if (wasAtBottom && _vm.DisplayedEntries.Count > 0)
            Dispatcher.UIThread.Post(
                () => LogListBox.ScrollIntoView(_vm.DisplayedEntries[^1]),
                DispatcherPriority.Render);
    }

    private void OnLogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.Control) return;

        switch (e.Key)
        {
            case Key.A:
                LogListBox.SelectAll();
                e.Handled = true;
                break;
            case Key.C:
                _ = CopyToClipboardAsync(selectedOnly: true);
                e.Handled = true;
                break;
        }
    }

    private async Task CopyToClipboardAsync(bool selectedOnly)
    {
        if (_vm is null) return;

        var entries = selectedOnly && LogListBox.SelectedItems?.Count > 0
            ? LogListBox.SelectedItems.Cast<LogEntry>()
            : _vm.DisplayedEntries.AsEnumerable();

        var text = string.Join(
            Environment.NewLine,
            entries.Select(e => $"{e.FormattedTimestamp}{e.DisplayText}"));

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(text);
    }
}
