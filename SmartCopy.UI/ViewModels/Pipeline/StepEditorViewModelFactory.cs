using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Settings;

namespace SmartCopy.UI.ViewModels.Pipeline;

public static class StepEditorViewModelFactory
{
    public static StepEditorViewModelBase Create(StepKind kind, IAppContext ctx)
    {
        return kind switch
        {
            StepKind.Copy => new CopyStepEditorViewModel(ctx),
            StepKind.Move => new MoveStepEditorViewModel(ctx),
            StepKind.Delete => new DeleteStepEditorViewModel(ctx.Settings),
            StepKind.Flatten => new FlattenStepEditorViewModel(),
            StepKind.Rename => new RenameStepEditorViewModel(),
            StepKind.Rebase => new RebaseStepEditorViewModel(),
            StepKind.SelectAll => new SelectAllStepEditorViewModel(),
            StepKind.InvertSelection => new InvertSelectionStepEditorViewModel(),
            StepKind.ClearSelection => new ClearSelectionStepEditorViewModel(),
            _ => throw new InvalidOperationException($"Unsupported step kind: {kind}"),
        };
    }
}
