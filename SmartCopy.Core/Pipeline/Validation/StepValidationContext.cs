using System.Linq;

namespace SmartCopy.Core.Pipeline.Validation;

/// <summary>
/// Flows through each step during pipeline validation, carrying current precondition
/// state and accumulating issues. Each step reads preconditions, records any issues,
/// then updates the context to reflect its post-conditions for subsequent steps.
/// </summary>
public sealed class StepValidationContext
{
    private readonly List<PipelineValidationIssue> _issues = new();

    public StepValidationContext(bool hasSelectedIncludedInputs, bool sourceExists = true)
    {
        HasSelectedIncludedInputs = hasSelectedIncludedInputs;
        SourceExists = sourceExists;
    }

    /// <summary>Index of the step currently being validated. Set by <see cref="PipelineValidator"/> before each call.</summary>
    public int StepIndex { get; internal set; }

    /// <summary>Whether the source still exists at this point in the pipeline (post-conditions of earlier steps may set this to false).</summary>
    public bool SourceExists { get; set; }

    /// <summary>Whether at least one selected/included file is present (supplied externally).</summary>
    public bool HasSelectedIncludedInputs { get; set; }

    /// <summary>True if any blocking issue has been recorded — subsequent steps should not validate.</summary>
    public bool HasBlockingIssue { get; private set; }

    /// <summary>Accumulated validation issues across all steps.</summary>
    public IReadOnlyList<PipelineValidationIssue> Issues => _issues;

    /// <summary>Adds a blocking issue scoped to the current step.</summary>
    public void AddBlockingIssue(string code, string message)
    {
        HasBlockingIssue = true;
        _issues.Add(new PipelineValidationIssue(
            StepIndex: StepIndex,
            Code: code,
            Message: message,
            Severity: PipelineValidationSeverity.Blocking));
    }

    /// <summary>Adds an issue that is pipeline-scoped (not attributed to a specific step).</summary>
    public void AddPipelineIssue(string code, string message, PipelineValidationSeverity severity)
    {
        HasBlockingIssue = true;

        _issues.Add(new PipelineValidationIssue(
            StepIndex: null,
            Code: code,
            Message: message,
            Severity: severity));
    }

    /// <summary>
    /// Called by a step that requires the source to still exist.
    /// Adds a <c>Step.SourceMissing</c> blocking issue scoped to the current step if it does not.
    /// </summary>
    public void ValidateSourceExists(string stepTypeName)
    {
        if (!SourceExists)
        {
            AddBlockingIssue("Step.SourceMissing", $"{stepTypeName} cannot run because the source no longer exists.");
        }
    }

    /// <summary>
    /// Called by a step that requires at least one selected/included input file.
    /// Adds a <c>Pipeline.NoSelectedInputs</c> blocking issue if none are available.
    /// </summary>
    public void ValidateHasSelectedInputs()
    {
        if (!HasSelectedIncludedInputs)
        {
            AddPipelineIssue("Pipeline.NoSelectedInputs", "At least one file must be selected.", PipelineValidationSeverity.Blocking);
        }
    }
}
