using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Threading;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class LogPanelView : UserControl
{
    private LogPanelViewModel? _vm;

    public LogPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.Entries.CollectionChanged -= OnEntriesChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as LogPanelViewModel;

        if (_vm is not null)
        {
            _vm.Entries.CollectionChanged += OnEntriesChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
            RebuildInlines();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LogPanelViewModel.MinimumLevel))
            RebuildInlines();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_vm is null) return;

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            RebuildInlines();
            return;
        }

        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null) return;

        var sv = LogScrollViewer;
        bool wasAtBottom = sv.Extent.Height - sv.Offset.Y <= sv.Viewport.Height + 2.0;

        foreach (LogEntry entry in e.NewItems)
        {
            if (entry.Level >= _vm.MinimumLevel)
                AppendEntryInlines(entry);
        }

        if (wasAtBottom)
            Dispatcher.UIThread.Post(() => sv.ScrollToEnd(), DispatcherPriority.Render);
    }

    private void RebuildInlines()
    {
        LogTextBlock.Inlines!.Clear();
        if (_vm is null) return;

        foreach (var entry in _vm.Entries)
        {
            if (entry.Level >= _vm.MinimumLevel)
                AppendEntryInlines(entry);
        }
    }

    private void AppendEntryInlines(LogEntry entry)
    {
        LogTextBlock.Inlines!.Add(new Run($"{entry.Timestamp:HH:mm:ss}  ")
        {
            Foreground = new SolidColorBrush(Color.Parse("#888888"))
        });
        LogTextBlock.Inlines!.Add(new Run($"{LevelPrefix(entry.Level)}{entry.Message}\n")
        {
            Foreground = new SolidColorBrush(Color.Parse(entry.ForegroundColor))
        });
    }

    private static string LevelPrefix(LogLevel level) => level switch
    {
        LogLevel.Warning => "[WARN]  ",
        LogLevel.Error   => "[ERR]   ",
        _                => ""
    };
}
