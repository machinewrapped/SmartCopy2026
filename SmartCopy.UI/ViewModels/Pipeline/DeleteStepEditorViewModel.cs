using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class DeleteStepEditorViewModel : StepEditorViewModelBase
{
    public IReadOnlyList<DeleteMode> DeleteModes { get; } = Enum.GetValues<DeleteMode>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPermanentDelete))]
    private DeleteMode _deleteMode = DeleteMode.Trash;

    public bool IsPermanentDelete => DeleteMode == DeleteMode.Permanent;

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
