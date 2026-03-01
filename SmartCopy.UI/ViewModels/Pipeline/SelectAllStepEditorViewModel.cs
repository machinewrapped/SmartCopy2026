using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.UI.ViewModels.Pipeline;

public sealed class SelectAllStepEditorViewModel : StepEditorViewModelBase
{
    public override bool IsValid => true;

    public override IPipelineStep BuildStep() => new SelectAllStep();

    public override void LoadFrom(PipelineStepViewModel stepViewModel) { }
}
