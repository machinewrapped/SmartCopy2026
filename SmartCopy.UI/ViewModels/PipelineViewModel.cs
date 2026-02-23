using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Pipeline.Validation;
using SmartCopy.UI.ViewModels.Pipeline;

namespace SmartCopy.UI.ViewModels;

public enum StepKind
{
    Flatten,
    Rebase,
    Rename,
    Convert,
    Copy,
    Move,
    Delete,
    Custom,
}

public enum StepCategory
{
    Path,
    Content,
    Executable,
}

public partial class PipelineStepViewModel : ViewModelBase
{
    private ITransformStep _step;
    private string? _validationMessage;
    private bool _hasValidationError;

    public PipelineStepViewModel(ITransformStep step)
    {
        _step = step;
    }

    public ITransformStep Step => _step;

    public StepKind Kind => ToKind(_step.StepType);

    public string Label => Kind switch
    {
        StepKind.Copy => "Copy To",
        StepKind.Move => "Move To",
        StepKind.Delete => "Delete",
        StepKind.Flatten => "Flatten",
        StepKind.Rename => "Rename",
        StepKind.Rebase => "Rebase",
        StepKind.Convert => "Convert",
        _ => _step.StepType,
    };

    public string Icon => Kind switch
    {
        StepKind.Copy => "→",
        StepKind.Move => "⇒",
        StepKind.Delete => "🗑",
        StepKind.Flatten => "⊞",
        StepKind.Rename => "✏",
        StepKind.Rebase => "⤢",
        StepKind.Convert => "⚙",
        _ => "?",
    };

    public string Details => _step switch
    {
        CopyStep copyStep => string.IsNullOrWhiteSpace(copyStep.DestinationPath)
            ? "Destination required"
            : copyStep.DestinationPath,
        MoveStep moveStep => string.IsNullOrWhiteSpace(moveStep.DestinationPath)
            ? "Destination required"
            : moveStep.DestinationPath,
        DeleteStep deleteStep => deleteStep.Mode == DeleteMode.Permanent ? "⚠ Permanent" : "Trash",
        FlattenStep flattenStep => $"Conflict: {flattenStep.ConflictStrategy}",
        RenameStep renameStep => $"Pattern: {renameStep.Pattern}",
        RebaseStep rebaseStep => $"Strip '{rebaseStep.StripPrefix}' Add '{rebaseStep.AddPrefix}'",
        ConvertStep convertStep => string.IsNullOrWhiteSpace(convertStep.OutputExtension)
            ? "Convert"
            : $"Convert to .{convertStep.OutputExtension}",
        _ => _step.StepType,
    };

    public bool HasDestination => _step is CopyStep or MoveStep;

    public string DestinationPath
    {
        get => _step switch
        {
            CopyStep copyStep => copyStep.DestinationPath,
            MoveStep moveStep => moveStep.DestinationPath,
            _ => string.Empty,
        };
        set
        {
            var destination = value ?? string.Empty;
            var changed = false;
            switch (_step)
            {
                case CopyStep copyStep when copyStep.DestinationPath != destination:
                    copyStep.DestinationPath = destination;
                    changed = true;
                    break;
                case MoveStep moveStep when moveStep.DestinationPath != destination:
                    moveStep.DestinationPath = destination;
                    changed = true;
                    break;
            }

            if (changed)
            {
                OnPropertyChanged();
                OnPropertyChanged(nameof(Details));
                StepChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool ShowDeleteBadge => _step is DeleteStep;

    public bool IsPermanentDelete => _step is DeleteStep { Mode: DeleteMode.Permanent };

    public string? DeleteBadge =>
        _step is DeleteStep deleteStep
            ? deleteStep.Mode == DeleteMode.Permanent ? "⚠ Permanent" : "Trash"
            : null;

    public string? ValidationMessage
    {
        get => _validationMessage;
        set => SetProperty(ref _validationMessage, value);
    }

    public bool HasValidationError
    {
        get => _hasValidationError;
        set => SetProperty(ref _hasValidationError, value);
    }

    public event EventHandler? StepChanged;

    public void ReplaceStep(ITransformStep newStep)
    {
        _step = newStep;
        OnPropertyChanged(nameof(Step));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(HasDestination));
        OnPropertyChanged(nameof(DestinationPath));
        OnPropertyChanged(nameof(ShowDeleteBadge));
        OnPropertyChanged(nameof(IsPermanentDelete));
        OnPropertyChanged(nameof(DeleteBadge));
        StepChanged?.Invoke(this, EventArgs.Empty);
    }

    private static StepKind ToKind(string stepType)
    {
        return stepType switch
        {
            "Flatten" => StepKind.Flatten,
            "Rebase" => StepKind.Rebase,
            "Rename" => StepKind.Rename,
            "Convert" => StepKind.Convert,
            "Copy" => StepKind.Copy,
            "Move" => StepKind.Move,
            "Delete" => StepKind.Delete,
            _ => StepKind.Custom,
        };
    }
}

public partial class PipelineViewModel : ViewModelBase
{
    private readonly PipelinePresetStore _presetStore;
    private readonly PipelineValidator _validator;
    private readonly string? _presetDirectory;

    public ObservableCollection<PipelineStepViewModel> Steps { get; } = new();
    public ObservableCollection<PipelinePreset> StandardPresets { get; } = new();
    public ObservableCollection<PipelinePreset> UserPresets { get; } = new();
    public AddStepViewModel AddStep { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    private PipelineValidationResult _validationResult = new([]);

    [ObservableProperty]
    private string? _blockingValidationMessage;

    public bool CanRun => ValidationResult.CanRun;

    public bool HasDeleteStep => Steps.Any(step => step.Step is DeleteStep);

    public string RunButtonLabel => HasDeleteStep ? "👁 Preview & Run" : "▶ Run";

    public string FirstDestinationPath
    {
        get
        {
            var step = Steps.FirstOrDefault(s => s.Step is CopyStep or MoveStep);
            return step?.DestinationPath ?? string.Empty;
        }
    }

    public event EventHandler? PipelineChanged;
    public event EventHandler? RunRequested;
    public event EventHandler? PreviewRequested;
    public event EventHandler<PipelineStepViewModel>? EditStepRequested;

    public PipelineViewModel(
        PipelinePresetStore? presetStore = null,
        PipelineValidator? validator = null,
        string? presetDirectory = null)
    {
        _presetStore = presetStore ?? new PipelinePresetStore();
        _validator = validator ?? new PipelineValidator();
        _presetDirectory = presetDirectory;

        Steps.CollectionChanged += OnStepsCollectionChanged;

        InitializePresetsInBackground();
        Revalidate();
    }

    public TransformPipeline BuildLivePipeline()
    {
        return new TransformPipeline(Steps.Select(step => step.Step));
    }

    public void AddStepFromResult(StepKind kind, ITransformStep step)
    {
        _ = kind;
        Steps.Add(new PipelineStepViewModel(step));
        Revalidate();
    }

    public void ReplaceStep(PipelineStepViewModel existing, ITransformStep replacement)
    {
        existing.ReplaceStep(replacement);
        Revalidate();
    }

    public void LoadPreset(PipelinePreset preset)
    {
        Steps.Clear();
        foreach (var configStep in preset.Config.Steps)
        {
            Steps.Add(new PipelineStepViewModel(PipelineStepFactory.FromConfig(configStep)));
        }

        Revalidate();
    }

    [RelayCommand]
    private void AddStepLegacy(StepKind kind)
    {
        AddStepFromResult(kind, CreateDefaultStep(kind));
    }

    [RelayCommand]
    private void RemoveStep(PipelineStepViewModel step)
    {
        if (step is null)
        {
            return;
        }

        Steps.Remove(step);
        Revalidate();
    }

    [RelayCommand]
    private async Task LoadPresetAsync(string name)
    {
        var all = await _presetStore.GetAllPresetsAsync(_presetDirectory);
        var preset = all.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            return;
        }

        LoadPreset(preset);
    }

    [RelayCommand]
    private async Task SavePipelineAsync(string? name = null)
    {
        if (Steps.Count == 0)
        {
            return;
        }

        var pipelineName = string.IsNullOrWhiteSpace(name)
            ? $"Pipeline {DateTime.Now:yyyy-MM-dd HHmmss}"
            : name.Trim();

        await _presetStore.SaveUserPresetAsync(
            pipelineName,
            ToConfig(pipelineName),
            _presetDirectory);

        await RefreshPresetsAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void RunPipeline()
    {
        if (HasDeleteStep)
        {
            PreviewRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        RunRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void PreviewPipeline()
    {
        PreviewRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RequestEditStep(PipelineStepViewModel step)
    {
        if (step is null)
        {
            return;
        }

        EditStepRequested?.Invoke(this, step);
    }

    private void OnStepsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
        {
            foreach (PipelineStepViewModel step in e.NewItems)
            {
                step.StepChanged += OnStepChanged;
            }
        }

        if (e.OldItems is not null)
        {
            foreach (PipelineStepViewModel step in e.OldItems)
            {
                step.StepChanged -= OnStepChanged;
            }
        }

        Revalidate();
    }

    private void OnStepChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        Revalidate();
    }

    private async void InitializePresetsInBackground()
    {
        try
        {
            await RefreshPresetsAsync();
        }
        catch
        {
        }
    }

    private async Task RefreshPresetsAsync()
    {
        var standards = await _presetStore.GetStandardPresetsAsync();
        var users = await _presetStore.GetUserPresetsAsync(_presetDirectory);

        StandardPresets.Clear();
        foreach (var preset in standards)
        {
            StandardPresets.Add(preset);
        }

        UserPresets.Clear();
        foreach (var preset in users)
        {
            UserPresets.Add(preset);
        }
    }

    private void Revalidate()
    {
        foreach (var step in Steps)
        {
            step.ValidationMessage = null;
            step.HasValidationError = false;
        }

        var result = _validator.Validate(Steps.Select(step => step.Step).ToList());
        ValidationResult = result;
        BlockingValidationMessage = result.FirstBlockingIssue?.Message;

        foreach (var issue in result.Issues.Where(i => i.StepIndex.HasValue))
        {
            var index = issue.StepIndex!.Value;
            if (index < 0 || index >= Steps.Count)
            {
                continue;
            }

            var step = Steps[index];
            if (string.IsNullOrWhiteSpace(step.ValidationMessage))
            {
                step.ValidationMessage = issue.Message;
                step.HasValidationError = issue.Severity == PipelineValidationSeverity.Blocking;
            }
        }

        OnPropertyChanged(nameof(FirstDestinationPath));
        OnPropertyChanged(nameof(HasDeleteStep));
        OnPropertyChanged(nameof(RunButtonLabel));
        RunPipelineCommand.NotifyCanExecuteChanged();
        PreviewPipelineCommand.NotifyCanExecuteChanged();
        PipelineChanged?.Invoke(this, EventArgs.Empty);
    }

    private PipelineConfig ToConfig(string name)
    {
        return new PipelineConfig(
            Name: name,
            Description: null,
            Steps: Steps.Select(step => step.Step.Config).ToList(),
            OverwriteMode: OverwriteMode.IfNewer.ToString(),
            DeleteMode: DeleteMode.Trash.ToString());
    }

    private static ITransformStep CreateDefaultStep(StepKind kind)
    {
        return kind switch
        {
            StepKind.Flatten => new FlattenStep(),
            StepKind.Rebase => new RebaseStep("", ""),
            StepKind.Rename => new RenameStep("{name}"),
            StepKind.Convert => new ConvertStep("mp3"),
            StepKind.Copy => new CopyStep(""),
            StepKind.Move => new MoveStep(""),
            StepKind.Delete => new DeleteStep(DeleteMode.Trash),
            _ => new FlattenStep(),
        };
    }
}
