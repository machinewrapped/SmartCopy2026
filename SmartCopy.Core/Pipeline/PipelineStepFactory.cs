using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.Core.Pipeline;

public static class PipelineStepFactory
{
    public static IPipelineStep FromConfig(TransformStepConfig config)
    {
        return config.StepType switch
        {
            StepKind.Copy => CopyStep.FromConfig(config),
            StepKind.Move => MoveStep.FromConfig(config),
            StepKind.Delete => DeleteStep.FromConfig(config),
            StepKind.Flatten => FlattenStep.FromConfig(config),
            StepKind.Rename => RenameStep.FromConfig(config),
            StepKind.SelectAll => new SelectAllStep(),
            StepKind.InvertSelection => new InvertSelectionStep(),
            StepKind.ClearSelection => new ClearSelectionStep(),
            StepKind.SaveSelectionToFile => SaveSelectionToFileStep.FromConfig(config),
            StepKind.AddSelectionFromFile => AddSelectionFromFileStep.FromConfig(config),
            StepKind.RemoveSelectionFromFile => RemoveSelectionFromFileStep.FromConfig(config),
            _ => throw new UnknownStepTypeException(config.StepType),
        };
    }
}
