using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class RebaseStepEditorViewModel : StepEditorViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _stripPrefix = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _addPrefix = string.Empty;

    public override bool IsValid =>
        !string.IsNullOrWhiteSpace(StripPrefix) || !string.IsNullOrWhiteSpace(AddPrefix);

    public override ITransformStep BuildStep() =>
        new RebaseStep(StripPrefix.Trim(), AddPrefix.Trim());

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is RebaseStep rebaseStep)
        {
            StripPrefix = rebaseStep.StripPrefix;
            AddPrefix = rebaseStep.AddPrefix;
        }
    }
}
