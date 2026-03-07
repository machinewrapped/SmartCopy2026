using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;

namespace SmartCopy.UI.ViewModels;

public partial class PreviewItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _sourcePath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDestination))]
    private string _destinationPath = string.Empty;

    [ObservableProperty]
    private SourceResult _sourceResult;

    [ObservableProperty]
    private DestinationResult _destinationResult;

    public bool HasDestination => !string.IsNullOrEmpty(DestinationPath);

    public string ActionText => GetActionText(SourceResult, DestinationResult);

    internal static string GetActionText(SourceResult source, DestinationResult destination) =>
        (source, destination) switch
        {
            (SourceResult.Copied, DestinationResult.Overwritten)    => "Copy (overwrite)",
            (SourceResult.Copied, _)                                => "Copy",
            (SourceResult.Moved,  DestinationResult.Overwritten)    => "Move (overwrite)",
            (SourceResult.Moved,  _)                                => "Move",
            (SourceResult.Trashed, _)                               => "Trash",
            (SourceResult.Deleted, _)                               => "Delete",
            _                                                       => string.Empty,
        };
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
    [ObservableProperty]
    private bool _isDeletePipeline;

    [ObservableProperty]
    private long _totalEstimatedInputBytes;

    [ObservableProperty]
    private long _totalEstimatedOutputBytes;

    [ObservableProperty]
    private int _totalActionCount;

    [ObservableProperty]
    private int _totalFilesAffected;

    [ObservableProperty]
    private int _totalFoldersAffected;

    public ObservableCollection<PreviewGroupViewModel> Groups { get; } = [];

    private enum GroupKey
    {
        Ready,
        Copy,
        Move,
        Overwrite,
        Delete,
    }

    public string ConfirmButtonText
    {
        get
        {
            if (IsDeletePipeline)
            {
                var filesText = TotalFoldersAffected > 0
                    ? $"{TotalFilesAffected} files, {TotalFoldersAffected} folders"
                    : $"{TotalFilesAffected} files";
                
                return $"⚠ Run ({filesText})";
            }
            else
            {
                return $"▶ Run ({TotalFilesAffected} files)";
            }
        }
    }

    public event Action? RunRequested;
    public event Action? CancelRequested;
    public event Func<string, Task>? SaveReportRequested;

    private OperationPlan? _currentPlan;

    public void LoadFrom(OperationPlan plan)
    {
        _currentPlan = plan;
        TotalActionCount = plan.Actions.Count;
        TotalFilesAffected = plan.TotalFilesAffected;
        TotalFoldersAffected = plan.TotalFoldersAffected;
        TotalEstimatedInputBytes = plan.TotalInputBytes;
        TotalEstimatedOutputBytes = plan.TotalEstimatedOutputBytes;

        Groups.Clear();
        var grouped = plan.Actions
            .GroupBy(ActionGrouping)
            .OrderBy(g => g.Key switch { GroupKey.Delete => 0, GroupKey.Overwrite => 1, _ => 2 });

        IsDeletePipeline = grouped.Select(g => g.Key == GroupKey.Delete).Any();

        foreach (var group in grouped)
        {
            var files = group.Sum(a => a.NumberOfFilesAffected);
            var folders = group.Sum(a => a.NumberOfFoldersAffected);

            var title = group.Key switch
            {
                GroupKey.Delete => FormatTitle("delete", files, folders),
                GroupKey.Overwrite => FormatTitle("overwrite", files, folders),
                GroupKey.Copy => FormatTitle("copy", files, folders),
                GroupKey.Move => FormatTitle("move", files, folders),
                _           => FormatTitle("ready", files, folders),
            };

            var isReady = group.Key == GroupKey.Ready;
            var vm = new PreviewGroupViewModel
            {
                Title = title,
                IsReadyGroup = isReady,
                IsExpanded = group.Key is GroupKey.Delete or GroupKey.Overwrite,
            };

            foreach (var action in group)
            {
                vm.Actions.Add(new PreviewItemViewModel
                {
                    SourcePath = action.SourcePath,
                    DestinationPath = action.DestinationPath ?? string.Empty,
                    SourceResult = action.SourceResult,
                    DestinationResult = action.DestinationResult,
                });
            }

            Groups.Add(vm);
        }

        RunCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ConfirmButtonText));
    }

    private static GroupKey ActionGrouping(PlannedAction a)
    {
        if (a.SourceResult is SourceResult.Trashed or SourceResult.Deleted)
            return GroupKey.Delete;

        if (a.DestinationResult == DestinationResult.Overwritten)
            return GroupKey.Overwrite;

        if (a.SourceResult == SourceResult.Moved)
            return GroupKey.Move;

        if (a.SourceResult == SourceResult.Copied)
            return GroupKey.Copy;
        
        return GroupKey.Ready;
    }

    private static string FormatTitle(string action, int files, int folders)
    {
        return (folders > 0) ? $"Will {action} {files} files and {folders} folders" : $"Will {action} {files} files";
    }

    [RelayCommand]
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
        sb.AppendLine($"**Files Affected:** {TotalFilesAffected}");
        sb.AppendLine($"**Folders Affected:** {TotalFoldersAffected}");
        sb.AppendLine($"**Total Input Size:** { FileSizeFormatter.FormatBytes(TotalEstimatedInputBytes)}");
        sb.AppendLine($"**Estimated Output Size:** {FileSizeFormatter.FormatBytes(TotalEstimatedOutputBytes)}");
        sb.AppendLine();

        var grouped = _currentPlan.Actions
            .GroupBy(ActionGrouping)
            .OrderBy(g => g.Key switch { GroupKey.Delete => 0, GroupKey.Overwrite => 1, _ => 2 });

        foreach (var group in grouped)
        {
            var files = group.Sum(a => a.NumberOfFilesAffected);
            var folders = group.Sum(a => a.NumberOfFoldersAffected);
            var title = group.Key switch
            {
                GroupKey.Delete => FormatTitle("delete", files, folders),
                GroupKey.Move   => FormatTitle("move", files, folders),
                GroupKey.Copy   => FormatTitle("copy", files, folders),
                GroupKey.Overwrite => FormatTitle("overwrite", files, folders),
                _               => FormatTitle("ready", files, folders),
            };

            sb.AppendLine($"## {title}");
            sb.AppendLine();
            sb.AppendLine("| Source | Destination | Action |");
            sb.AppendLine("|---|---|---|");

            foreach (var action in group)
            {
                var actionText = PreviewItemViewModel.GetActionText(action.SourceResult, action.DestinationResult);
                sb.AppendLine($"| `{action.SourcePath}` | `{action.DestinationPath}` | {actionText} |");
            }
            sb.AppendLine();
        }

        await SaveReportRequested.Invoke(sb.ToString());
    }
}
