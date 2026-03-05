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

    public bool IsConfigurable => Step.IsConfigurable;

    public bool HasDestination => Step is IHasDestinationPath pathProvider && pathProvider.HasDestinationPath;

    public string DestinationPath => (Step as IHasDestinationPath)?.DestinationPath ?? string.Empty;

    public void SetDestinationPath(string? destinationPath)
    {
        if (Step is IHasDestinationPath pathProvider)
        {
            pathProvider.ChangeDestinationPath(destinationPath ?? string.Empty);

            OnPropertyChanged();
            OnPropertyChanged(nameof(Label));
            OnPropertyChanged(nameof(HasDestination));
            OnPropertyChanged(nameof(DestinationPath));

            StepChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool ShowDeleteBadge => Step is DeleteStep { Mode: DeleteMode.Permanent };

    public string? DeleteBadge => ShowDeleteBadge ? "⚠ Permanent delete" : null;

    [ObservableProperty]
    public string? _validationMessage;

    [ObservableProperty]
    public bool _hasValidationError;

    public event EventHandler? StepChanged;

    public void ReplaceStep(IPipelineStep newStep, string? customName = null)
    {
        Step = newStep;
        CustomName = string.IsNullOrWhiteSpace(customName) ? null : customName.Trim();
        OnPropertyChanged(nameof(Step));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(CustomName));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(HasDestination));
        OnPropertyChanged(nameof(DestinationPath));
        OnPropertyChanged(nameof(ShowDeleteBadge));
        OnPropertyChanged(nameof(DeleteBadge));
        StepChanged?.Invoke(this, EventArgs.Empty);
    }
}
