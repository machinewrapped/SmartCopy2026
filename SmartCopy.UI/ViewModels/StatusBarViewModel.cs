namespace SmartCopy.UI.ViewModels;

public class StatusBarViewModel : ViewModelBase
{
    public SelectionViewModel Selection { get; } = new();
    public OperationProgressViewModel Progress { get; } = new();
}
