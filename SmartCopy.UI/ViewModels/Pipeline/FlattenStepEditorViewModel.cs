using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class FlattenStepEditorViewModel : StepEditorViewModelBase
{
    public IReadOnlyList<FlattenConflictStrategy> ConflictStrategies { get; } =
        Enum.GetValues<FlattenConflictStrategy>();

    [ObservableProperty]
    private FlattenConflictStrategy _conflictStrategy = FlattenConflictStrategy.AutoRenameCounter;

    public override bool IsValid => true;

    public override IPipelineStep BuildStep() => new FlattenStep(ConflictStrategy);

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is FlattenStep flattenStep)
        {
            ConflictStrategy = flattenStep.ConflictStrategy;
        }
    }
}
