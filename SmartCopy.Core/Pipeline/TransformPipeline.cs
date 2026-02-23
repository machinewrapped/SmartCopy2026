using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartCopy.Core.Pipeline;

public sealed class TransformPipeline
{
    private readonly List<ITransformStep> _steps = [];

    public TransformPipeline(IEnumerable<ITransformStep> steps)
    {
        _steps.AddRange(steps);
    }

    public IReadOnlyList<ITransformStep> Steps => _steps;

    public bool HasDeleteStep => _steps.Any(step => step.StepType == "Delete");

    public void Validate()
    {
        if (_steps.Count == 0)
        {
            throw new InvalidOperationException("Pipeline must contain at least one step.");
        }

        var executableSteps = _steps.Where(step => step.IsExecutable);
        if (!executableSteps.Any())
        {
            throw new InvalidOperationException("Pipeline must contain at least one executable step.");
        }

        if (!_steps[^1].IsExecutable)
        {
            throw new InvalidOperationException("The final step must be executable.");
        }
    }
}
