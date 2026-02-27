using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline;

public interface ITransformStep
{
    StepKind StepType { get; }
    bool IsExecutable { get; }
    TransformStepConfig Config { get; }

    /// <summary>
    /// Validates this step within the current pipeline state. The step should:
    /// <list type="bullet">
    ///   <item>Read preconditions from <paramref name="context"/> (e.g. <see cref="StepValidationContext.SourceExists"/>).</item>
    ///   <item>Call <see cref="StepValidationContext.AddBlockingIssue"/> for any configuration or precondition failures.</item>
    ///   <item>Update post-conditions on <paramref name="context"/> (e.g. set <see cref="StepValidationContext.SourceExists"/> to <c>false</c> if the step consumes the source).</item>
    /// </list>
    /// </summary>
    void Validate(StepValidationContext context);

    TransformResult Preview(TransformContext context);
    Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct);
}
