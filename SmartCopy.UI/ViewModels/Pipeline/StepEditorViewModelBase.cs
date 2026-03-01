using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.Pipeline;

namespace SmartCopy.UI.ViewModels.Pipeline;

public abstract partial class StepEditorViewModelBase : ObservableObject
{
    public abstract bool IsValid { get; }

    public abstract IPipelineStep BuildStep();

    public abstract void LoadFrom(PipelineStepViewModel stepViewModel);
}
