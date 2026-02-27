using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline;

public interface ITransformStep
{
    StepKind StepType { get; }
    bool IsExecutable { get; }
    TransformStepConfig Config { get; }

    TransformResult Preview(TransformContext context);
    Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct);

    /// <summary>
    /// Validates this step within the pipeline.
    /// <list type="bullet">
    ///   <item>Validate preconditions from <paramref name="context"/>.</item>
    ///   <item>Update postconditions on <paramref name="context"/>.</item>
    /// </list>
    /// </summary>
    void Validate(StepValidationContext context);

}
