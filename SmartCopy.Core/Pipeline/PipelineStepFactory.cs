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
            StepKind.Rename => new RenameStep(config.GetRequired("pattern")),
            StepKind.Rebase => new RebaseStep(
                config.GetOptional("stripPrefix"),
                config.GetOptional("addPrefix")),
            StepKind.SelectAll => new SelectAllStep(),
            StepKind.InvertSelection => new InvertSelectionStep(),
            StepKind.ClearSelection => new ClearSelectionStep(),
            _ => throw new UnknownStepTypeException(config.StepType),
        };
    }
}
