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
            (SourceResult.Skipped, _)                               => "Skip",
            _                                                       => string.Empty,
        };
}

public partial class PreviewGroupViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _title = string.Empty;

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

    [ObservableProperty]
    private int _totalFilesSkipped;

    [ObservableProperty]
    private int _totalFoldersSkipped;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasWarnings))]
    private IReadOnlyList<string> _warnings = [];

    public bool HasWarnings => Warnings.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors))]
    [NotifyPropertyChangedFor(nameof(HasNoErrors))]
    private IReadOnlyList<string> _errors = [];

    public bool HasErrors => Errors.Count > 0;
    public bool HasNoErrors => !HasErrors;

    [ObservableProperty]
    private bool _isPreparingPlan;

    [ObservableProperty]
    private string _preparationMessage = "Preparing preview\u2026";

    public ObservableCollection<PreviewGroupViewModel> Groups { get; } = [];

    private enum GroupKey
    {
        Copy,
        Move,
        Overwrite,
        Delete,
        Skip,
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

    /// <summary>
    /// Puts the view into "preparing" state: shows the progress indicator
    /// and clears any previously-loaded plan content.
    /// </summary>
    public void BeginPreparation(string message = "Preparing preview\u2026")
    {
        IsPreparingPlan = true;
        PreparationMessage = message;
        _currentPlan = null;
        TotalActionCount = 0;
        TotalFilesAffected = 0;
        TotalFoldersAffected = 0;
        TotalFilesSkipped = 0;
        TotalFoldersSkipped = 0;
        TotalEstimatedInputBytes = 0;
        TotalEstimatedOutputBytes = 0;
        Warnings = [];
        Errors = [];
        Groups.Clear();
        OnPropertyChanged(nameof(ConfirmButtonText));
    }

    public void LoadFrom(OperationPlan plan)
    {
        IsPreparingPlan = false;
        _currentPlan = plan;
        TotalActionCount = plan.Actions.Count;
        TotalFilesAffected = plan.TotalFilesAffected;
        TotalFoldersAffected = plan.TotalFoldersAffected;
        TotalEstimatedInputBytes = plan.TotalInputBytes;
        TotalEstimatedOutputBytes = plan.TotalEstimatedOutputBytes;
        Warnings = plan.Warnings;
        Errors = plan.Errors;

        Groups.Clear();

        var deleteActions = new List<PlannedAction>();
        var overwriteActions = new List<PlannedAction>();
        var moveActions = new List<PlannedAction>();
        var copyActions = new List<PlannedAction>();
        var skipActions = new List<PlannedAction>();

        foreach (var a in plan.Actions)
        {
            if (a.SourceResult is SourceResult.Trashed or SourceResult.Deleted) deleteActions.Add(a);
            if (a.DestinationResult == DestinationResult.Overwritten) overwriteActions.Add(a);
            if (a.SourceResult == SourceResult.Moved) moveActions.Add(a);
            if (a.SourceResult == SourceResult.Copied) copyActions.Add(a);
            if (a.SourceResult == SourceResult.Skipped) skipActions.Add(a);
        }

        IsDeletePipeline = deleteActions.Count > 0;

        void AddGroup(GroupKey key, List<PlannedAction> actions)
        {
            if (actions.Count == 0) return;

            var files = key == GroupKey.Skip ? actions.Sum(a => a.NumberOfFilesSkipped) : actions.Sum(a => a.NumberOfFilesAffected);
            var folders = key == GroupKey.Skip ? actions.Sum(a => a.NumberOfFoldersSkipped) : actions.Sum(a => a.NumberOfFoldersAffected);

            var title = FormatTitle(key.ToString().ToLowerInvariant(), files, folders);

            var vm = new PreviewGroupViewModel
            {
                Title = title,
                IsExpanded = key is GroupKey.Delete or GroupKey.Overwrite,
            };

            foreach (var action in actions)
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

        AddGroup(GroupKey.Delete, deleteActions);
        AddGroup(GroupKey.Overwrite, overwriteActions);
        AddGroup(GroupKey.Move, moveActions);
        AddGroup(GroupKey.Copy, copyActions);
        AddGroup(GroupKey.Skip, skipActions);

        RunCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ConfirmButtonText));
    }

    private static string FormatTitle(string action, int files, int folders)
    {
        return (folders > 0) ? $"Will {action} {files} files and {folders} folders" : $"Will {action} {files} files";
    }

    [RelayCommand(CanExecute = nameof(HasNoErrors))]
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
        sb.AppendLine($"**Files Skipped:** {TotalFilesSkipped}");
        sb.AppendLine($"**Folders Affected:** {TotalFoldersAffected}");
        sb.AppendLine($"**Folders Skipped:** {TotalFoldersSkipped}");
        sb.AppendLine($"**Total Input Size:** { FileSizeFormatter.FormatBytes(TotalEstimatedInputBytes)}");
        sb.AppendLine($"**Estimated Output Size:** {FileSizeFormatter.FormatBytes(TotalEstimatedOutputBytes)}");
        sb.AppendLine();

        var deleteActions = new List<PlannedAction>();
        var overwriteActions = new List<PlannedAction>();
        var moveActions = new List<PlannedAction>();
        var copyActions = new List<PlannedAction>();
        var skipActions = new List<PlannedAction>();

        foreach (var a in _currentPlan.Actions)
        {
            if (a.SourceResult is SourceResult.Trashed or SourceResult.Deleted) deleteActions.Add(a);
            if (a.DestinationResult == DestinationResult.Overwritten) overwriteActions.Add(a);
            if (a.SourceResult == SourceResult.Moved) moveActions.Add(a);
            if (a.SourceResult == SourceResult.Copied) copyActions.Add(a);
            if (a.SourceResult == SourceResult.Skipped) skipActions.Add(a);
        }

        void WriteGroup(GroupKey key, List<PlannedAction> actions)
        {
            if (actions.Count == 0) return;

            var files = key == GroupKey.Skip ? actions.Sum(a => a.NumberOfFilesSkipped) : actions.Sum(a => a.NumberOfFilesAffected);
            var folders = key == GroupKey.Skip ? actions.Sum(a => a.NumberOfFoldersSkipped) : actions.Sum(a => a.NumberOfFoldersAffected);
            var title = FormatTitle(key.ToString().ToLowerInvariant(), files, folders);

            sb.AppendLine($"## {title}");
            sb.AppendLine();
            sb.AppendLine("| Source | Destination | Action |");
            sb.AppendLine("|---|---|---|");

            foreach (var action in actions)
            {
                var actionText = PreviewItemViewModel.GetActionText(action.SourceResult, action.DestinationResult);
                sb.AppendLine($"| `{action.SourcePath}` | `{action.DestinationPath}` | {actionText} |");
            }
            sb.AppendLine();
        }

        WriteGroup(GroupKey.Delete, deleteActions);
        WriteGroup(GroupKey.Overwrite, overwriteActions);
        WriteGroup(GroupKey.Move, moveActions);
        WriteGroup(GroupKey.Copy, copyActions);
        WriteGroup(GroupKey.Skip, skipActions);

        await SaveReportRequested.Invoke(sb.ToString());
    }
}
