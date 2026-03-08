using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class DeleteStepEditorViewModel : StepEditorViewModelBase
{
    // Default to full capabilities so no false-positive warning when no source is configured yet.
    private ProviderCapabilities _sourceCapabilities = ProviderCapabilities.Full;

    public IReadOnlyList<DeleteMode> DeleteModes { get; } = Enum.GetValues<DeleteMode>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPermanentDelete))]
    [NotifyPropertyChangedFor(nameof(CapabilityWarning))]
    private DeleteMode _deleteMode = DeleteMode.Trash;

    public DeleteStepEditorViewModel(AppSettings settings)
    {
        DeleteMode = settings.DefaultDeleteMode;
    }

    public void SetSourceCapabilities(ProviderCapabilities capabilities)
    {
        _sourceCapabilities = capabilities;
        OnPropertyChanged(nameof(CapabilityWarning));
    }

    public bool IsPermanentDelete => DeleteMode == DeleteMode.Permanent;

    public string? CapabilityWarning => DeleteMode == DeleteMode.Trash && !_sourceCapabilities.CanTrash
        ? "Network paths cannot be sent to Trash — permanent delete will be used"
        : null;

    public override bool IsValid => true;

    public override IPipelineStep BuildStep() => new DeleteStep(DeleteMode);

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is DeleteStep deleteStep)
        {
            DeleteMode = deleteStep.Mode;
        }
    }
}
