using System.Collections.Generic;

namespace SmartCopy.Core.Pipeline.Validation;

internal sealed record PipelineStepContract(
    bool IsExecutable,
    bool RequiresSourceExists,
    bool RequiresDestinationPath = false,
    bool? SetsSourceExists = null);

public static class PipelineStepContracts
{
    private static readonly IReadOnlyDictionary<string, PipelineStepContract> Contracts =
        new Dictionary<string, PipelineStepContract>
        {
            ["Copy"] = new(IsExecutable: true, RequiresSourceExists: true, RequiresDestinationPath: true, SetsSourceExists: true),
            ["Move"] = new(IsExecutable: true, RequiresSourceExists: true, RequiresDestinationPath: true, SetsSourceExists: false),
            ["Delete"] = new(IsExecutable: true, RequiresSourceExists: true, SetsSourceExists: false),
            ["Flatten"] = new(IsExecutable: false, RequiresSourceExists: true),
            ["Rename"] = new(IsExecutable: false, RequiresSourceExists: true),
            ["Rebase"] = new(IsExecutable: false, RequiresSourceExists: true),
            ["Convert"] = new(IsExecutable: false, RequiresSourceExists: true),
        };

    internal static bool TryGet(string stepType, out PipelineStepContract contract)
    {
        return Contracts.TryGetValue(stepType, out contract!);
    }
}
