using System.Collections.Generic;
using System.Linq;

namespace SmartCopy.Core.Pipeline.Validation;

public sealed class PipelineValidationResult
{
    public PipelineValidationResult(IReadOnlyList<PipelineValidationIssue> issues)
    {
        Issues = issues;
    }

    public IReadOnlyList<PipelineValidationIssue> Issues { get; }

    public bool CanRun => !Issues.Any(issue => issue.Severity == PipelineValidationSeverity.Blocking);

    public PipelineValidationIssue? FirstBlockingIssue =>
        Issues.FirstOrDefault(issue => issue.Severity == PipelineValidationSeverity.Blocking);
}
