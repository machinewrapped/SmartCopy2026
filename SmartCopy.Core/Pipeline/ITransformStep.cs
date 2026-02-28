using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline;

public interface ITransformStep
{
    StepKind StepType { get; }
    bool IsExecutable { get; }

    /// <summary>
    /// Whether the step has configurable parameters. Steps that return <see langword="false"/>
    /// are added to the pipeline directly without opening the editor dialog.
    /// </summary>
    bool IsConfigurable => true;

    /// <summary>
    /// Whether this step needs all filter-included nodes as its input, not just the
    /// currently-selected nodes. True for <c>SelectAll</c> and <c>InvertSelection</c>,
    /// which must see every filter-included file to operate correctly.
    /// </summary>
    bool ProvidesInput => false;

    TransformStepConfig Config { get; }

    IAsyncEnumerable<TransformResult> PreviewAsync(TransformContext context, CancellationToken ct);
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
