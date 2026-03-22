using CommunityToolkit.Mvvm.ComponentModel;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;

namespace SmartCopy.UI.ViewModels.Pipeline;

public abstract partial class StepEditorViewModelBase : ObservableObject
{
    public abstract bool IsValid { get; }

    public abstract IPipelineStep BuildStep();

    public abstract void LoadFrom(PipelineStepViewModel stepViewModel);

    /// <summary>
    /// Returns a context-aware display name for the step being configured.
    /// Override to provide a friendly name using path context (e.g. showing the filename).
    /// Default: delegates to <see cref="IPipelineStep.AutoSummary"/>.
    /// </summary>
    public virtual string GetAutoName(IPathResolver resolver) => BuildStep().AutoSummary;
}
