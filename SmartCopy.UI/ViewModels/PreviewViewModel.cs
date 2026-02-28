using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Pipeline;

namespace SmartCopy.UI.ViewModels;

public partial class PreviewItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    private string _destinationPath = string.Empty;

    [ObservableProperty]
    private string _action = string.Empty;

    [ObservableProperty]
    private PlanWarning? _warning;
}

public partial class PreviewGroupViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private bool _isReadyGroup;

    [ObservableProperty]
    private bool _isExpanded;

    public ObservableCollection<PreviewItemViewModel> Actions { get; } = [];

    public int Count => Actions.Count;
}

public partial class PreviewViewModel : ViewModelBase
{
    private DeleteMode _deleteMode = DeleteMode.Trash;

    [ObservableProperty]
    private bool _isDeletePipeline;

    [ObservableProperty]
    private long _totalEstimatedOutputBytes;

    [ObservableProperty]
    private int _totalActionCount;

    public ObservableCollection<PreviewGroupViewModel> Groups { get; } = [];

    public bool CanRun => true;

    public string ConfirmButtonText
    {
        get
        {
            if (!IsDeletePipeline)
            {
                return $"▶ Run ({TotalActionCount} actions)";
            }

            return _deleteMode == DeleteMode.Permanent
                ? $"⚠ Permanently Delete {TotalActionCount}"
                : $"🗑 Delete {TotalActionCount} files to Bin";
        }
    }

    public event Action? RunRequested;
    public event Action? CancelRequested;
    public event Func<string, Task>? SaveReportRequested;

    private OperationPlan? _currentPlan;

    public void LoadFrom(OperationPlan plan, bool isDeletePipeline, DeleteMode deleteMode)
    {
        _currentPlan = plan;
        _deleteMode = deleteMode;
        IsDeletePipeline = isDeletePipeline;
        TotalActionCount = plan.Actions.Count;
        TotalEstimatedOutputBytes = plan.TotalEstimatedOutputBytes;

        Groups.Clear();
        var grouped = plan.Actions
            .GroupBy(action => action.Warning)
            .OrderBy(group => group.Key.HasValue ? 0 : 1);

        foreach (var group in grouped)
        {
            var title = group.Key switch
            {
                PlanWarning.DestinationOverwritten => $"Destination Overwritten ({group.Count()})",
                PlanWarning.SourceWillBeRemoved => $"Will be removed ({group.Count()})",
                PlanWarning.NameConflict => $"Name Conflict ({group.Count()})",
                PlanWarning.PermissionIssue => $"Permission Issue ({group.Count()})",
                _ => $"Ready ({group.Count()})",
            };

            var isReady = !group.Key.HasValue;
            var vm = new PreviewGroupViewModel
            {
                Title = title,
                IsReadyGroup = isReady,
                IsExpanded = isReady || group.Key == PlanWarning.SourceWillBeRemoved
            };

            foreach (var action in group)
            {
                vm.Actions.Add(new PreviewItemViewModel
                {
                    SourcePath = action.SourcePath,
                    DestinationPath = action.DestinationPath,
                    Action = action.StepSummary,
                    Warning = action.Warning,
                });
            }

            Groups.Add(vm);
        }

        RunCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ConfirmButtonText));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Run()
    {
        RunRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        CancelRequested?.Invoke();
    }

    [RelayCommand]
    private async Task SaveReportAsync()
    {
        if (_currentPlan is null || SaveReportRequested is null) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# SmartCopy2026 Preview Report");
        sb.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine($"**Total Actions:** {TotalActionCount}");
        sb.AppendLine($"**Estimated Output Size:** {TotalEstimatedOutputBytes} bytes");
        sb.AppendLine();

        var grouped = _currentPlan.Actions
            .GroupBy(action => action.Warning)
            .OrderBy(group => group.Key.HasValue ? 0 : 1);

        foreach (var group in grouped)
        {
            var title = group.Key switch
            {
                PlanWarning.DestinationOverwritten => $"Destination Overwritten ({group.Count()})",
                PlanWarning.SourceWillBeRemoved => $"Will be removed ({group.Count()})",
                PlanWarning.NameConflict => $"Name Conflict ({group.Count()})",
                PlanWarning.PermissionIssue => $"Permission Issue ({group.Count()})",
                _ => $"Ready ({group.Count()})",
            };

            sb.AppendLine($"## {title}");
            sb.AppendLine();
            sb.AppendLine("| Source | Destination | Action |");
            sb.AppendLine("|---|---|---|");

            foreach (var action in group)
            {
                sb.AppendLine($"| `{action.SourcePath}` | `{action.DestinationPath}` | {action.StepSummary} |");
            }
            sb.AppendLine();
        }

        await SaveReportRequested.Invoke(sb.ToString());
    }
}
