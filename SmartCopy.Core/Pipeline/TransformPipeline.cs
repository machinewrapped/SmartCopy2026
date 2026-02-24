using System;
using System.Collections.Generic;
using System.Linq;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline;

public sealed class TransformPipeline
{
    private readonly List<ITransformStep> _steps = [];

    public TransformPipeline(IEnumerable<ITransformStep> steps)
    {
        _steps.AddRange(steps);
    }

    public IReadOnlyList<ITransformStep> Steps => _steps;

    public bool HasDeleteStep => _steps.Any(step => step.StepType == StepKind.Delete);

    public void Validate(PipelineValidationContext? context = null)
    {
        var result = PipelineValidator.Validate(_steps, context);
        if (!result.CanRun)
        {
            var issue = result.FirstBlockingIssue;
            throw new InvalidOperationException(issue?.Message ?? "Pipeline is invalid.");
        }
    }
}
