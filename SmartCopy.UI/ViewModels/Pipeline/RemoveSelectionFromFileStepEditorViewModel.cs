using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

public partial class RemoveSelectionFromFileStepEditorViewModel : SelectionFromFileStepEditorViewModelBase
{
    public RemoveSelectionFromFileStepEditorViewModel(IAppContext ctx) : base(ctx)
    {
    }

    public override IPipelineStep BuildStep() =>
        new RemoveSelectionFromFileStep(FilePath.Trim());

    public override string GetAutoName(IPathResolver resolver) =>
        string.IsNullOrWhiteSpace(FilePath)
            ? BuildStep().AutoSummary
            : $"Remove from {resolver.GetFileName(FilePath)}";

    public override void LoadFrom(PipelineStepViewModel stepViewModel)
    {
        if (stepViewModel.Step is RemoveSelectionFromFileStep step)
            FilePath = step.FilePath;
    }
}
