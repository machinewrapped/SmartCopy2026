using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartCopy.Core.FileSystem;
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
    private readonly ILogger<PipelineViewModel> _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private readonly IAppContext _appContext;
    private readonly PipelinePresetStore _presetStore;
    private readonly StepPresetStore _stepPresetStore;
    private readonly AppSettings _appSettings;
    private int _selectedIncludedFileCount;
    private long _selectedBytes;
    private IFileSystemProvider? _sourceProvider;
    private FreeSpaceCache _cachedFreeSpace = new();
    private CancellationTokenSource _revalidateCts = new();

    public ObservableCollection<PipelineStepViewModel> Steps { get; } = [];

    [ObservableProperty]
    private PipelineStepViewModel? _selectedStep;
    public ObservableCollection<PipelinePreset> UserPresets { get; } = [];
    public AddStepViewModel AddStep { get; }

    public bool HasSteps => Steps.Count > 0;

    public StepPresetStore StepPresetStore => _stepPresetStore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    private PipelineValidationResult _validationResult = new([]);

    [ObservableProperty]
    private string? _blockingValidationMessage;

    private bool _capabilityBlocked;

    private bool _isRunning = false;
    public bool IsRunning 
    {
        get => _isRunning;
        set 
        {
            SetProperty(ref _isRunning, value);
            OnPropertyChanged(nameof(IsNotRunning));
            OnPropertyChanged(nameof(CanRun));
            UpdateButtonStates();
        }
    }

    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set 
        { 
            SetProperty(ref _isScanning, value); 
            OnPropertyChanged(nameof(CanRun));
            UpdateButtonStates(); 
        }
    }

    public bool IsNotRunning => !IsRunning;

    public bool CanRun => ValidationResult.CanRun && !_capabilityBlocked && !IsRunning && !IsScanning;

    public bool HasDeleteStep => Steps.Any(step => step.Step is DeleteStep);

    public bool HasOverwriteStep => Steps.Any(step => 
        (step.Step is CopyStep copy && copy.OverwriteMode != OverwriteMode.Skip) ||
        (step.Step is MoveStep move && move.OverwriteMode != OverwriteMode.Skip));

    public string RunButtonLabel => HasDeleteStep || HasOverwriteStep ? "⚠ Run" : "▶ Run";

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
    public event EventHandler<PipelineStepViewModel>? SwapSourceRequested;
    public event EventHandler? SavePipelineRequested;

    public PipelineViewModel(IAppContext appContext, ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory;
        _logger = loggerFactory?.CreateLogger<PipelineViewModel>() ?? NullLogger<PipelineViewModel>.Instance;
        _appContext = appContext;
        _appSettings = appContext.Settings;
        _presetStore = new PipelinePresetStore(appContext.DataStore.GetDirectoryPath("Pipelines"), loggerFactory?.CreateLogger<PipelinePresetStore>());
        _stepPresetStore = new StepPresetStore(appContext.DataStore.GetFilePath("step-presets.json"), loggerFactory?.CreateLogger<StepPresetStore>());

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

    private async void OnStepPresetPicked(StepPreset preset)
    {
        try
        {
            var step = PipelineStepFactory.FromConfig(preset.Config);
            await AddStepFromResult(step, preset.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnStepPresetPicked failed");
        }
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
            _logger.LogError(ex, "Failed to delete pipeline preset '{Name}'", name);
        }
    }

    public TransformPipeline BuildLivePipeline()
    {
        return new TransformPipeline(Steps.Select(step => step.Step));
    }

    public async Task<bool> TryAddStepWithoutConfiguration(StepKind kind)
    {
        var step = StepEditorViewModelFactory.Create(kind, _appSettings).BuildStep();
        if (step.IsConfigurable) return false;
        await AddStepFromResult(step);
        return true;
    }

    public async Task AddStepFromResult(IPipelineStep step, string? customName = null)
    {
        Steps.Add(new PipelineStepViewModel(step, customName));
        await RefreshFreeSpaceCacheAsync();
        Revalidate();
    }

    public async Task ReplaceStep(PipelineStepViewModel existing, IPipelineStep replacement, string? customName = null)
    {
        existing.ReplaceStep(replacement, customName);
        await RefreshFreeSpaceCacheAsync();
        Revalidate();
    }

    public async Task LoadPreset(PipelinePreset preset)
    {
        Steps.Clear();
        foreach (var configStep in preset.Config.Steps)
        {
            var customName = GetOptionalParameter(configStep, CustomNameParameter);
            Steps.Add(new PipelineStepViewModel(PipelineStepFactory.FromConfig(configStep), customName));
        }

        await RefreshFreeSpaceCacheAsync();
        Revalidate();
    }

    [RelayCommand(CanExecute = nameof(IsNotRunning))]
    private async Task RemoveStep(PipelineStepViewModel step)
    {
        if (step is null)
        {
            return;
        }

        Steps.Remove(step);

        await RefreshFreeSpaceCacheAsync();
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

        await LoadPreset(preset);
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

    public void RequestSwapWithSource(PipelineStepViewModel step)
    {
        if (IsRunning || !step.HasDestination) return;
        SwapSourceRequested?.Invoke(this, step);
    }

    [RelayCommand(CanExecute = nameof(IsNotRunning))]
    private void RequestEditStep(PipelineStepViewModel step)
    {
        if (step is null)
        {
            return;
        }

        EditStepRequested?.Invoke(this, step);
    }

    internal AppSettings AppSettings => _appSettings;

    // Default to full capabilities so editors show no false-positive warning before source is set.
    internal ProviderCapabilities SourceCapabilities { get; private set; } = ProviderCapabilities.Full;

    internal async Task SetSourceCapabilities(ProviderCapabilities capabilities)
    {
        SourceCapabilities = capabilities;
        await RefreshFreeSpaceCacheAsync();
        Revalidate();
    }

    internal async Task SetSourceContext(IFileSystemProvider provider)
    {
        _sourceProvider = provider;
        await SetSourceCapabilities(provider.Capabilities);
    }

    internal void SetSelectedBytes(long bytes)
    {
        var normalized = Math.Max(0, bytes);
        if (_selectedBytes == normalized) return;
        _selectedBytes = normalized;
        Revalidate();
    }

    internal void RecordRecentTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        _appSettings.RecentTargets.Remove(path);
        _appSettings.RecentTargets.Insert(0, path);

        if (_appSettings.RecentTargets.Count > MaxRecentTargets)
            _appSettings.RecentTargets.RemoveAt(MaxRecentTargets);
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

    private async void OnStepChanged(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        try
        {
            await RefreshFreeSpaceCacheAsync();
            Revalidate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnStepChanged failed");
        }
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

    /// <summary>
    /// Marks the step at <paramref name="index"/> as the currently-executing step
    /// and clears the flag on any previously active step. Safe to call from any thread.
    /// </summary>
    public void SetActiveStep(int index)
    {
        for (int i = 0; i < Steps.Count; i++)
        {
            Steps[i].IsActiveStep = (i == index);
        }
    }

    /// <summary>
    /// Clears the active-step highlight from all steps.
    /// Called after execution completes or is cancelled.
    /// </summary>
    public void ClearActiveStep()
    {
        foreach (var step in Steps)
            step.IsActiveStep = false;
    }

    private async void Revalidate()
    {
        _revalidateCts.Cancel();
        _revalidateCts = new CancellationTokenSource();
        var ct = _revalidateCts.Token;

        foreach (var step in Steps)
        {
            step.ValidationMessage = null;
            step.HasValidationError = false;
            step.HasValidationWarning = false;
            step.TrashUnavailable = false;
        }

        _capabilityBlocked = false;

        try
        {
            var result = await PipelineValidator.ValidateAsync(
                [.. Steps.Select(step => step.Step)],
                new PipelineValidationContext(
                    SourceProvider:   _sourceProvider,
                    ProviderRegistry: _appContext,
                    CachedFreeSpace:  _cachedFreeSpace,
                    HasSelectedIncludedInputs: _selectedIncludedFileCount > 0,
                    SelectedBytes:    _selectedBytes),
                ct);

            ct.ThrowIfCancellationRequested();

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
                    step.HasValidationWarning = issue.Severity == PipelineValidationSeverity.Warning;
                }
            }

            // Capability-derived blocking: Trash mode is unavailable for this source path.
            if (!SourceCapabilities.CanTrash)
            {
                foreach (var step in Steps)
                {
                    if (step.Step is DeleteStep ds && ds.Mode == DeleteMode.Trash)
                    {
                        step.TrashUnavailable = true;
                        _capabilityBlocked = true;
                    }
                }
            }

            if (_capabilityBlocked && BlockingValidationMessage is null)
                BlockingValidationMessage = "Trash is not available for this path";

            OnPropertyChanged(nameof(FirstDestinationPath));
            OnPropertyChanged(nameof(HasDeleteStep));
            OnPropertyChanged(nameof(HasOverwriteStep));
            OnPropertyChanged(nameof(RunButtonLabel));
            OnPropertyChanged(nameof(HasSteps));
            UpdateButtonStates();

            PipelineChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer validation; discard this result.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Revalidate failed");
        }
    }

    private async Task RefreshFreeSpaceCacheAsync(CancellationToken ct = default)
    {
        var cache = new FreeSpaceCache();
        foreach (var stepVm in Steps)
        {
            if (stepVm.Step is IHasDestinationPath destination)
            {
                await cache.CacheForDestinationAsync(destination, _appContext, ct);
            }
        }
        _cachedFreeSpace = cache;
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
        RemoveStepCommand.NotifyCanExecuteChanged();
        RequestEditStepCommand.NotifyCanExecuteChanged();
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
