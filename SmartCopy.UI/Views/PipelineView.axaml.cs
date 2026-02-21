using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Threading;
using SmartCopy.UI.ViewModels;

namespace SmartCopy.UI.Views;

public partial class PipelineView : UserControl
{
    private PipelineViewModel? _currentViewModel;

    public PipelineView()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (_currentViewModel != null)
                _currentViewModel.Steps.CollectionChanged -= Steps_CollectionChanged;

            _currentViewModel = DataContext as PipelineViewModel;

            if (_currentViewModel != null)
                _currentViewModel.Steps.CollectionChanged += Steps_CollectionChanged;
        };
    }

    private void Steps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(
                () => StepsScrollViewer?.ScrollToEnd(),
                DispatcherPriority.Loaded);
        }
    }
}
