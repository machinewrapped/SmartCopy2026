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

        var terminalSteps = _steps.Where(step => step.IsTerminal).ToList();
        if (terminalSteps.Count != 1)
        {
            throw new InvalidOperationException("Pipeline must contain exactly one terminal step.");
        }

        if (!_steps[^1].IsTerminal)
        {
            throw new InvalidOperationException("The terminal step must be the final step in the pipeline.");
        }
    }
}
