using System.Collections.Generic;

namespace SmartCopy.Core.Pipeline.Validation;

internal sealed record PipelineStepContract(
    bool IsExecutable,
    bool RequiresSourceExists,
    bool RequiresDestinationPath = false,
    bool? SetsSourceExists = null);

public static class PipelineStepContracts
{
    private static readonly Dictionary<StepKind, PipelineStepContract> Contracts =
        new()
        {
            [StepKind.Copy] = new(IsExecutable: true, RequiresSourceExists: true, RequiresDestinationPath: true, SetsSourceExists: true),
            [StepKind.Move] = new(IsExecutable: true, RequiresSourceExists: true, RequiresDestinationPath: true, SetsSourceExists: false),
            [StepKind.Delete] = new(IsExecutable: true, RequiresSourceExists: true, SetsSourceExists: false),
            [StepKind.Flatten] = new(IsExecutable: false, RequiresSourceExists: true),
            [StepKind.Rename] = new(IsExecutable: false, RequiresSourceExists: true),
            [StepKind.Rebase] = new(IsExecutable: false, RequiresSourceExists: true),
            [StepKind.Convert] = new(IsExecutable: false, RequiresSourceExists: true),
        };

    internal static bool TryGet(StepKind stepType, out PipelineStepContract contract)
    {
        return Contracts.TryGetValue(stepType, out contract!);
    }
}
