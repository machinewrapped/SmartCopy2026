using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class DeleteStepEditorViewModel : StepEditorViewModelBase
{
    public IReadOnlyList<DeleteMode> DeleteModes { get; } = Enum.GetValues<DeleteMode>();

    [ObservableProperty]
    private DeleteMode _deleteMode = DeleteMode.Trash;

    public override bool IsValid => true;

    public override ITransformStep BuildStep() => new DeleteStep(DeleteMode);

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is DeleteStep deleteStep)
        {
            DeleteMode = deleteStep.Mode;
        }
    }
}
