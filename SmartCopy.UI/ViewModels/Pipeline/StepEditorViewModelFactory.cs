namespace SmartCopy.UI.ViewModels.Pipeline;

public static class StepEditorViewModelFactory
{
    public static StepEditorViewModelBase Create(StepKind kind)
    {
        return kind switch
        {
            StepKind.Copy => new CopyStepEditorViewModel(),
            StepKind.Move => new MoveStepEditorViewModel(),
            StepKind.Delete => new DeleteStepEditorViewModel(),
            StepKind.Flatten => new FlattenStepEditorViewModel(),
            StepKind.Rename => new RenameStepEditorViewModel(),
            StepKind.Rebase => new RebaseStepEditorViewModel(),
            StepKind.Convert => new ConvertStepEditorViewModel(),
            _ => throw new InvalidOperationException($"Unsupported step kind: {kind}"),
        };
    }
}
