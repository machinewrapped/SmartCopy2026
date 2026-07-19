using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class MoveStepEditorViewModel : CopyMoveStepEditorViewModelBase
{
    public MoveStepEditorViewModel(IAppContext ctx) : base(ctx)
    {
    }

    public override IPipelineStep BuildStep() => new MoveStep(DestinationPath.Trim(), SelectedOverwriteMode);

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is MoveStep moveStep)
        {
            DestinationPath = moveStep.DestinationPath ?? string.Empty;
            SelectedOverwriteMode = moveStep.OverwriteMode;
        }
    }
}
