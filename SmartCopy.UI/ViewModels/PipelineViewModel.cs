using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Pipeline.Validation;
using SmartCopy.Core.Settings;
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
    private string _customName = string.Empty;
    private string? _validationMessage;
    private bool _hasValidationError;

    public PipelineStepViewModel(ITransformStep step, string? customName = null)
    {
        _step = step;
        _customName = PipelineStepDisplay.NormalizeCustomName(customName);
    }

    public ITransformStep Step => _step;

    public StepKind Kind => ToKind(_step.StepType);

    public string? CustomName => string.IsNullOrWhiteSpace(_customName) ? null : _customName;

    public string AutoSummary => PipelineStepDisplay.GetSummary(_step);

    public string Summary => string.IsNullOrWhiteSpace(_customName)
        ? AutoSummary
        : _customName;

    public string Description => PipelineStepDisplay.GetDescription(_step);

    // Keep old names for compatibility with tests and any remaining bindings.
    public string Label => Summary;

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

    // Keep old names for compatibility with tests and any remaining bindings.
    public string Details => Description;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

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
                OnPropertyChanged(nameof(AutoSummary));
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(Label));
                OnPropertyChanged(nameof(Details));
                OnPropertyChanged(nameof(Description));
                OnPropertyChanged(nameof(HasDescription));
                OnPropertyChanged(nameof(ShowDeleteBadge));
                OnPropertyChanged(nameof(DeleteBadge));
                StepChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool ShowDeleteBadge => _step is DeleteStep { Mode: DeleteMode.Permanent };

    public bool IsPermanentDelete => _step is DeleteStep { Mode: DeleteMode.Permanent };

    public string? DeleteBadge =>
        _step is DeleteStep deleteStep
            ? deleteStep.Mode == DeleteMode.Permanent ? "⚠ Permanent delete" : null
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

    public void ReplaceStep(ITransformStep newStep, string? customName = null)
    {
        _step = newStep;
        _customName = PipelineStepDisplay.NormalizeCustomName(customName);
        OnPropertyChanged(nameof(Step));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(CustomName));
        OnPropertyChanged(nameof(AutoSummary));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(HasDestination));
        OnPropertyChanged(nameof(DestinationPath));
        OnPropertyChanged(nameof(ShowDeleteBadge));
        OnPropertyChanged(nameof(IsPermanentDelete));
        OnPropertyChanged(nameof(DeleteBadge));
        StepChanged?.Invoke(this, EventArgs.Empty);
    }

    internal static StepKind ToKind(string stepType)
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
    private const string CustomNameParameter = "customName";
    private readonly PipelinePresetStore _presetStore;
    private readonly PipelineValidator _validator;
    private readonly string? _presetDirectory;
    private readonly StepPresetStore _stepPresetStore;

    public ObservableCollection<PipelineStepViewModel> Steps { get; } = new();
    public ObservableCollection<PipelinePreset> StandardPresets { get; } = new();
    public ObservableCollection<PipelinePreset> UserPresets { get; } = new();
    public AddStepViewModel AddStep { get; }

    public StepPresetStore StepPresetStore => _stepPresetStore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    private PipelineValidationResult _validationResult = new([]);

    [ObservableProperty]
    private string? _blockingValidationMessage;

    public bool CanRun => ValidationResult.CanRun;

    public bool HasDeleteStep => Steps.Any(step => step.Step is DeleteStep);

    public string RunButtonLabel => HasDeleteStep ? "⚠ Run" : "▶ Run";

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
        string? presetDirectory = null,
        StepPresetStore? stepPresetStore = null,
        AppSettings? appSettings = null,
        string? stepPresetStorePath = null)
    {
        _presetStore = presetStore ?? new PipelinePresetStore();
        _validator = validator ?? new PipelineValidator();
        _presetDirectory = presetDirectory;
        _stepPresetStore = stepPresetStore ?? new StepPresetStore();

        AddStep = new AddStepViewModel(_stepPresetStore, appSettings, stepPresetStorePath);
        AddStep.StepPresetPicked += OnStepPresetPicked;

        Steps.CollectionChanged += OnStepsCollectionChanged;

        InitializePresetsInBackground();
        Revalidate();
    }

    private void OnStepPresetPicked(StepPreset preset)
    {
        var step = PipelineStepFactory.FromConfig(preset.Config);
        var autoName = PipelineStepDisplay.GetSummary(step);
        var customName = string.Equals(preset.Name, autoName, StringComparison.OrdinalIgnoreCase)
            ? null
            : preset.Name;
        AddStepFromResult(PipelineStepViewModel.ToKind(step.StepType), step, customName);
    }

    public TransformPipeline BuildLivePipeline()
    {
        return new TransformPipeline(Steps.Select(step => step.Step));
    }

    public void AddStepFromResult(StepKind kind, ITransformStep step, string? customName = null)
    {
        _ = kind;
        Steps.Add(new PipelineStepViewModel(step, customName));
        Revalidate();
    }

    public void ReplaceStep(PipelineStepViewModel existing, ITransformStep replacement, string? customName = null)
    {
        existing.ReplaceStep(replacement, customName);
        Revalidate();
    }

    public void LoadPreset(PipelinePreset preset)
    {
        Steps.Clear();
        foreach (var configStep in preset.Config.Steps)
        {
            var customName = GetOptionalParameter(configStep, CustomNameParameter);
            Steps.Add(new PipelineStepViewModel(PipelineStepFactory.FromConfig(configStep), customName));
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
            Steps: Steps.Select(BuildConfigWithUiMetadata).ToList(),
            OverwriteMode: OverwriteMode.IfNewer.ToString(),
            DeleteMode: DeleteMode.Trash.ToString());
    }

    private static TransformStepConfig BuildConfigWithUiMetadata(PipelineStepViewModel stepViewModel)
    {
        var baseConfig = stepViewModel.Step.Config;
        var parameters = baseConfig.Parameters.DeepClone() as JsonObject ?? new JsonObject();
        if (string.IsNullOrWhiteSpace(stepViewModel.CustomName))
        {
            parameters.Remove(CustomNameParameter);
        }
        else
        {
            parameters[CustomNameParameter] = stepViewModel.CustomName;
        }

        return new TransformStepConfig(baseConfig.StepType, parameters);
    }

    private static string? GetOptionalParameter(TransformStepConfig config, string name)
    {
        var value = config.Parameters[name]?.GetValue<string>()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
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
