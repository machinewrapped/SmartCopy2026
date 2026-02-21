using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class FilterChainView : UserControl
{
    private FilterChainViewModel? _currentViewModel;

    public FilterChainView()
    {
        InitializeComponent();

        this.DataContextChanged += (s, e) =>
        {
            if (_currentViewModel != null)
            {
                _currentViewModel.Filters.CollectionChanged -= Filters_CollectionChanged;
            }

            _currentViewModel = DataContext as FilterChainViewModel;

            if (_currentViewModel != null)
            {
                _currentViewModel.Filters.CollectionChanged += Filters_CollectionChanged;
            }
        };
    }

    private void Filters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(() =>
            {
                FiltersScrollViewer?.ScrollToEnd();
            }, DispatcherPriority.Loaded);
        }
    }
}
