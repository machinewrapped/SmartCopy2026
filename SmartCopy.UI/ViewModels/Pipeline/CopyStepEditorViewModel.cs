using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class CopyStepEditorViewModel : CopyMoveStepEditorViewModelBase
{
    public CopyStepEditorViewModel(IAppContext ctx) : base(ctx)
    {
    }

    public override IPipelineStep BuildStep() => new CopyStep(DestinationPath.Trim(), SelectedOverwriteMode);

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is CopyStep copyStep)
        {
            DestinationPath = copyStep.DestinationPath ?? string.Empty;
            SelectedOverwriteMode = copyStep.OverwriteMode;
        }
    }
}
