using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline;

public interface ITransformStep
{
    StepKind StepType { get; }
    bool IsExecutable { get; }
    TransformStepConfig Config { get; }

    /// <summary>Whether this step requires the source to still exist before it runs.</summary>
    bool RequiresSourceExists { get; }

    /// <summary>Whether this step requires at least one selected/included input file.</summary>
    bool RequiresSelectedIncludedInputs { get; }

    /// <summary>
    /// How this step affects source availability for subsequent steps.
    /// <c>true</c> = source remains, <c>false</c> = source is consumed/destroyed, <c>null</c> = no effect.
    /// </summary>
    bool? SetsSourceExists { get; }

    /// <summary>Returns step-scoped validation issues (e.g. missing configuration).</summary>
    IEnumerable<PipelineValidationIssue> Validate(int stepIndex);

    TransformResult Preview(TransformContext context);
    Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct);
}

