using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class MoveStepEditorViewModel : StepEditorViewModelBase
{
    public IReadOnlyList<OverwriteMode> OverwriteModes { get; } = Enum.GetValues<OverwriteMode>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _destinationPath = string.Empty;

    [ObservableProperty]
    private OverwriteMode _overwriteMode = OverwriteMode.IfNewer;

    public override bool IsValid => !string.IsNullOrWhiteSpace(DestinationPath);

    public override ITransformStep BuildStep() => new MoveStep(DestinationPath.Trim());

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is MoveStep moveStep)
        {
            DestinationPath = moveStep.DestinationPath;
        }
    }
}
