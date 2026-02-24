using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class ConvertStepEditorViewModel : StepEditorViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValid))]
    private string _outputExtension = "mp3";

    public override bool IsValid => !string.IsNullOrWhiteSpace(OutputExtension);

    public override ITransformStep BuildStep() => new ConvertStep(OutputExtension.Trim());

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is ConvertStep convertStep)
        {
            OutputExtension = convertStep.OutputExtension;
        }
    }
}
