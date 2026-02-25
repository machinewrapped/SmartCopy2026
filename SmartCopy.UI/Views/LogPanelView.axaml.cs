using System.Collections.Specialized;
using Avalonia.Controls;
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
            _vm.Entries.CollectionChanged -= OnEntriesChanged;

        _vm = DataContext as LogPanelViewModel;

        if (_vm is not null)
            _vm.Entries.CollectionChanged += OnEntriesChanged;
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
            return;

        // Sample position before the new item causes the content to grow.
        var sv = LogScrollViewer;
        bool wasAtBottom = sv.Extent.Height - sv.Offset.Y <= sv.Viewport.Height + 2.0;

        if (wasAtBottom)
            Dispatcher.UIThread.Post(() => sv.ScrollToEnd(), DispatcherPriority.Render);
    }
}
