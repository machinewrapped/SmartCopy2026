using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.UI.ViewModels;

public partial class PipelineStepViewModel : ViewModelBase
{
    public PipelineStepViewModel(IPipelineStep step, string? customName = null)
    {
        Step = step;
        if (customName is not null && customName != step.AutoSummary)
        {
            CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName.Trim();
        }
    }

    public IPipelineStep Step { get; private set; }

    public StepKind Kind => Step.StepType;

    public string Label => CustomName ?? Step.AutoSummary;

    public string? CustomName { get; private set; }

    public string Description => Step.Description;

    public string Icon => Kind.GetIcon();

    public bool IsConfigurable => Step.IsConfigurable;

    public bool HasDestination => Step is IHasDestinationPath pathProvider && pathProvider.HasDestinationPath;

    public string DestinationPath => (Step as IHasDestinationPath)?.DestinationPath ?? string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDeleteBadge))]
    [NotifyPropertyChangedFor(nameof(DeleteBadge))]
    private bool _trashUnavailable;

    public bool ShowDeleteBadge => Step is DeleteStep { Mode: DeleteMode.Permanent } || TrashUnavailable;

    public string? DeleteBadge => TrashUnavailable
        ? "⚠ Trash unavailable"
        : ShowDeleteBadge ? "⚠ Permanent delete" : null;

    [ObservableProperty]
    public string? _validationMessage;

    [ObservableProperty]
    public bool _hasValidationError;

    [ObservableProperty]
    public bool _isActiveStep;

    public event EventHandler? StepChanged;

    public void ReplaceStep(IPipelineStep newStep, string? customName = null)
    {
        Step = newStep;
        CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName.Trim();
        OnPropertyChanged(nameof(Step));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(CustomName));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasDestination));
        OnPropertyChanged(nameof(DestinationPath));
        OnPropertyChanged(nameof(ShowDeleteBadge));
        OnPropertyChanged(nameof(DeleteBadge));
        StepChanged?.Invoke(this, EventArgs.Empty);
    }
}
