using System;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.Core.Pipeline;

public static class PipelineStepFactory
{
    public static ITransformStep FromConfig(TransformStepConfig config)
    {
        return config.StepType switch
        {
            StepKind.Copy => new CopyStep(GetRequiredString(config, "destinationPath")),
            StepKind.Move => new MoveStep(GetRequiredString(config, "destinationPath")),
            StepKind.Delete => new DeleteStep(ParseDeleteMode(config)),
            StepKind.Flatten => new FlattenStep(ParseFlattenConflictStrategy(config)),
            StepKind.Rename => new RenameStep(GetRequiredString(config, "pattern")),
            StepKind.Rebase => new RebaseStep(
                GetOptionalString(config, "stripPrefix"),
                GetOptionalString(config, "addPrefix")),
            StepKind.SelectAll => new SelectAllStep(),
            StepKind.InvertSelection => new InvertSelectionStep(),
            StepKind.ClearSelection => new ClearSelectionStep(),
            _ => throw new UnknownStepTypeException(config.StepType),
        };
    }

    private static string GetRequiredString(TransformStepConfig config, string name)
    {
        var value = GetOptionalString(config, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"Step '{config.StepType}' requires non-empty parameter '{name}'.",
                nameof(config));
        }

        return value;
    }

    private static string GetOptionalString(TransformStepConfig config, string name)
    {
        return config.Parameters[name]?.GetValue<string>()?.Trim() ?? string.Empty;
    }

    private static DeleteMode ParseDeleteMode(TransformStepConfig config)
    {
        var raw = GetOptionalString(config, "deleteMode");
        return Enum.TryParse<DeleteMode>(raw, ignoreCase: true, out var mode)
            ? mode
            : DeleteMode.Trash;
    }

    private static FlattenConflictStrategy ParseFlattenConflictStrategy(TransformStepConfig config)
    {
        var raw = GetOptionalString(config, "conflictStrategy");
        return Enum.TryParse<FlattenConflictStrategy>(raw, ignoreCase: true, out var strategy)
            ? strategy
            : FlattenConflictStrategy.AutoRenameCounter;
    }
}
