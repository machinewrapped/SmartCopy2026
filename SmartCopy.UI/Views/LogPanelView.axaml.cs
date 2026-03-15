using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class LogPanelView : UserControl
{
    private LogPanelViewModel? _vm;

    private readonly IBrush _timestampBrush;
    private readonly IBrush _infoBrush;
    private readonly IBrush _warningBrush;
    private readonly IBrush _errorBrush;

    public LogPanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        LogScrollViewer.AddHandler(KeyDownEvent, OnLogKeyDown, RoutingStrategies.Tunnel);

        var res = Application.Current!.Resources;
        _timestampBrush = (IBrush)res["LogTimestampBrush"]!;
        _infoBrush      = (IBrush)res["DefaultForegroundBrush"]!;
        _warningBrush   = (IBrush)res["LogWarningBrush"]!;
        _errorBrush     = (IBrush)res["LogErrorBrush"]!;
    }

    private void OnLogKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control)
        {
            if (e.Key == Key.A)
            {
                LogTextBlock.SelectAll();
                e.Handled = true;
            }            
        }
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
        if (e.PropertyName == nameof(LogPanelViewModel.FilterLevel))
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
            if (_vm.FilterLevel == null || entry.Level == _vm.FilterLevel)
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
            if (_vm.FilterLevel == null || entry.Level == _vm.FilterLevel)
                AppendEntryInlines(entry);
        }
    }

    private void AppendEntryInlines(LogEntry entry)
    {
        LogTextBlock.Inlines!.Add(new Run($"{entry.Timestamp:HH:mm:ss}  ")
        {
            Foreground = _timestampBrush
        });

        var messageBrush = entry.Level switch
        {
            LogLevel.Warning => _warningBrush,
            LogLevel.Error   => _errorBrush,
            _                => _infoBrush
        };

        LogTextBlock.Inlines!.Add(new Run($"{LevelPrefix(entry.Level)}{entry.Message}\n")
        {
            Foreground = messageBrush
        });
    }

    private static string LevelPrefix(LogLevel level) => level switch
    {
        LogLevel.Warning => "[WARN]  ",
        LogLevel.Error   => "[ERR]   ",
        _                => ""
    };
}
