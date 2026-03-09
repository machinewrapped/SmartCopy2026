using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Represents an individual step in a workflow pipeline.
/// </summary>
public interface IPipelineStep
{
    /// <summary>The type of this step.</summary>
    StepKind StepType { get; }

    /// <summary>Whether this step can be executed.</summary>
    bool IsExecutable { get; }

    /// <summary>Whether this step has any configurable parameters.</summary>
    bool IsConfigurable => true;

    /// <summary>A short summary of the actions this step will perform.</summary>
    string AutoSummary { get; }

    /// <summary>A more verbose description of the actions the step will take </summary>
    string Description { get; }

    /// <summary>Serializable configuration for this step.</summary>
    TransformStepConfig Config { get; }

    /// <summary>Preview the results of this step in a pipeline job.</summary>
    IAsyncEnumerable<TransformResult> PreviewAsync(IStepContext context, CancellationToken ct);

    /// <summary>Apply this step to a pipeline job.</summary>
    IAsyncEnumerable<TransformResult> ApplyAsync(IStepContext context, CancellationToken ct);

    /// <summary>Validates whether this step is logically valid within the pipeline.</summary>
    Task Validate(StepValidationContext context, CancellationToken ct = default);
}
