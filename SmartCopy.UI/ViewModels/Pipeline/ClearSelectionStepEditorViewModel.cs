using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.UI.ViewModels.Pipeline;

public sealed class ClearSelectionStepEditorViewModel : StepEditorViewModelBase
{
    public override bool IsValid => true;

    public override ITransformStep BuildStep() => new ClearSelectionStep();

    public override void LoadFrom(PipelineStepViewModel stepViewModel) { }
}
