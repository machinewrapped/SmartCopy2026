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

public enum StepCategory
{
    Executable,
    Path,
    Content,
    Selection,
}

public partial class PipelineViewModel : ViewModelBase
{
    private const string CustomNameParameter = "customName";
    private const int MaxRecentTargets = 10;
    private readonly IAppContext _appContext;
    private readonly PipelinePresetStore _presetStore;
    private readonly StepPresetStore _stepPresetStore;
    private readonly AppSettings _appSettings;
    private int _selectedIncludedFileCount;

    public ObservableCollection<PipelineStepViewModel> Steps { get; } = [];

    [ObservableProperty]
    private PipelineStepViewModel? _selectedStep;
    public ObservableCollection<PipelinePreset> UserPresets { get; } = [];
    public AddStepViewModel AddStep { get; }

    public bool HasSteps => Steps.Count > 0;

    public StepPresetStore StepPresetStore => _stepPresetStore;

    internal AppSettings AppSettings => _appSettings;

    internal void RecordRecentTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        _appSettings.RecentTargets.Remove(path);
        _appSettings.RecentTargets.Insert(0, path);

        if (_appSettings.RecentTargets.Count > MaxRecentTargets)
            _appSettings.RecentTargets.RemoveAt(MaxRecentTargets);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    private PipelineValidationResult _validationResult = new([]);

    [ObservableProperty]
    private string? _blockingValidationMessage;

    private bool _isRunning = false;
    public bool IsRunning 
    {
        get => _isRunning;
        set 
        {
            SetProperty(ref _isRunning, value);
            UpdateButtonStates();
        }
    }

    public bool CanRun => ValidationResult.CanRun && !IsRunning;

    public bool HasDeleteStep => Steps.Any(step => step.Step is DeleteStep);

    public string RunButtonLabel => HasDeleteStep ? "⚠ Run" : "▶ Run";

    public string FirstDestinationPath
    {
        get
        {
            var step = Steps.FirstOrDefault(s => s.Step is IHasDestinationPath);
            return (step?.Step as IHasDestinationPath)?.DestinationPath ?? string.Empty;
        }
    }

    public event EventHandler? PipelineChanged;
    public event EventHandler? RunRequested;
    public event EventHandler? PreviewRequested;
    public event EventHandler<PipelineStepViewModel>? EditStepRequested;
    public event EventHandler? SavePipelineRequested;

    public PipelineViewModel(IAppContext appContext)
    {
        _appContext = appContext;
        _appSettings = appContext.Settings;
        _presetStore = new PipelinePresetStore(appContext.DataStore.GetDirectoryPath("Pipelines"));
        _stepPresetStore = new StepPresetStore(appContext.DataStore.GetFilePath("step-presets.json"));

        AddStep = new AddStepViewModel(_appContext);
        AddStep.StepPresetPicked += OnStepPresetPicked;

        Steps.CollectionChanged += OnStepsCollectionChanged;

        InitializePresetsInBackground();
        Revalidate();
    }

    public void SetSelectedIncludedFileCount(int selectedIncludedFileCount)
    {
        var normalizedCount = Math.Max(0, selectedIncludedFileCount);
        if (_selectedIncludedFileCount == normalizedCount)
        {
            return;
        }

        _selectedIncludedFileCount = normalizedCount;
        Revalidate();
    }

    private void OnStepPresetPicked(StepPreset preset)
    {
        var step = PipelineStepFactory.FromConfig(preset.Config);
        AddStepFromResult(step, preset.Name);
    }

    public async Task DeletePipelinePresetAsync(string name)
    {
        try
        {
            await _presetStore.DeleteUserPresetAsync(name);
            await RefreshPresetsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ERROR] Failed to delete pipeline preset '{name}': {ex.Message}");
        }
    }

    public TransformPipeline BuildLivePipeline()
    {
        return new TransformPipeline(Steps.Select(step => step.Step));
    }

    public bool TryAddStepWithoutConfiguration(StepKind kind)
    {
        var step = StepEditorViewModelFactory.Create(kind, _appSettings).BuildStep();
        if (step.IsConfigurable) return false;
        AddStepFromResult(step);
        return true;
    }

    public void AddStepFromResult(IPipelineStep step, string? customName = null)
    {
        Steps.Add(new PipelineStepViewModel(step, customName));
        Revalidate();
    }

    public void ReplaceStep(PipelineStepViewModel existing, IPipelineStep replacement, string? customName = null)
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
        var all = await _presetStore.GetUserPresetsAsync();
        var preset = all.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            return;
        }

        LoadPreset(preset);
    }

    [RelayCommand]
    private void SavePipeline()
    {
        SavePipelineRequested?.Invoke(this, EventArgs.Empty);
    }

    public async Task SavePipelineAsync(string name)
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
            ToConfig(pipelineName));

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
        var users = await _presetStore.GetUserPresetsAsync();

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

        var result = PipelineValidator.Validate(
            [.. Steps.Select(step => step.Step)],
            new PipelineValidationContext(_selectedIncludedFileCount > 0));
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
        OnPropertyChanged(nameof(HasSteps));
        UpdateButtonStates();

        PipelineChanged?.Invoke(this, EventArgs.Empty);
    }

    internal PipelineConfig ToConfig(string name)
    {
        return new PipelineConfig(
            Name: name,
            Description: null,
            Steps: [.. Steps.Select(BuildConfigWithUiMetadata)]);
    }

    private void UpdateButtonStates()
    {
        RunPipelineCommand.NotifyCanExecuteChanged();
        PreviewPipelineCommand.NotifyCanExecuteChanged();
    }

    private static TransformStepConfig BuildConfigWithUiMetadata(PipelineStepViewModel stepViewModel)
    {
        var baseConfig = stepViewModel.Step.Config;
        var parameters = baseConfig.Parameters.DeepClone() as JsonObject ?? [];
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
}
