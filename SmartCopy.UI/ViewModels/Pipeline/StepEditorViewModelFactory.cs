using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

public static class StepEditorViewModelFactory
{
    public static StepEditorViewModelBase Create(StepKind kind, AppSettings? settings = null)
    {
        return kind switch
        {
            StepKind.Copy => new CopyStepEditorViewModel(settings),
            StepKind.Move => new MoveStepEditorViewModel(settings),
            StepKind.Delete => new DeleteStepEditorViewModel(),
            StepKind.Flatten => new FlattenStepEditorViewModel(),
            StepKind.Rename => new RenameStepEditorViewModel(),
            StepKind.Rebase => new RebaseStepEditorViewModel(),
            StepKind.Convert => new ConvertStepEditorViewModel(),
            _ => throw new InvalidOperationException($"Unsupported step kind: {kind}"),
        };
    }
}
