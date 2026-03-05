using System.Collections.Generic;
using System.Threading;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline;

public record PipelineStepDisplayInfo(string Summary, string Description);

public interface IPipelineStep
{
    StepKind StepType { get; }
    PipelineStepDisplayInfo Display { get; }
    bool IsExecutable { get; }

    /// <summary>
    /// Whether this step has configurable parameters. Steps that return <see langword="false"/>
    /// are added to the pipeline directly without opening the editor dialog.
    /// </summary>
    bool IsConfigurable => true;

    TransformStepConfig Config { get; }

    IAsyncEnumerable<TransformResult> PreviewAsync(IStepContext context, CancellationToken ct);
    IAsyncEnumerable<TransformResult> ApplyAsync(IStepContext context, CancellationToken ct);

    /// <summary>
    /// Validates this step within the pipeline.
    /// <list type="bullet">
    ///   <item>Validate preconditions from <paramref name="context"/>.</item>
    ///   <item>Update postconditions on <paramref name="context"/>.</item>
    /// </list>
    /// </summary>
    void Validate(StepValidationContext context);
}
