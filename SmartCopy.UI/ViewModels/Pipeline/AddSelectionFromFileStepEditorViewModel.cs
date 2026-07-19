using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class AddSelectionFromFileStepEditorViewModel : SelectionFromFileStepEditorViewModelBase
{
    public AddSelectionFromFileStepEditorViewModel(IAppContext ctx) : base(ctx)
    {
    }

    public override IPipelineStep BuildStep() =>
        new AddSelectionFromFileStep(FilePath.Trim());

    public override string GetAutoName(IPathResolver resolver) =>
        string.IsNullOrWhiteSpace(FilePath)
            ? BuildStep().AutoSummary
            : $"Add from {resolver.GetFileName(FilePath)}";

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is AddSelectionFromFileStep step)
            FilePath = step.FilePath;
    }
}
