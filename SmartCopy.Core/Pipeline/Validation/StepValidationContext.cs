using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline.Validation;

/// <summary>
/// Flows through each step during pipeline validation, carrying current precondition
/// state and accumulating issues. Each step reads preconditions, records any issues,
/// then updates the context to reflect its post-conditions for subsequent steps.
/// </summary>
public sealed class StepValidationContext
{
    private readonly List<PipelineValidationIssue> _issues = new();

    // Mutable shadow copy so each step sees free space reduced by earlier steps' consumption.
    private readonly FreeSpaceCache? _cachedFreeSpace;

    public StepValidationContext(
        bool hasSelectedIncludedInputs,
        bool sourceExists = true,
        long selectedBytes = 0,
        IFileSystemProvider? sourceProvider = null,
        IPathResolver? providerRegistry = null,
        FreeSpaceCache? cachedFreeSpace = null)
    {
        HasSelectedIncludedInputs = hasSelectedIncludedInputs;
        SourceExists = sourceExists;
        SelectedBytes = selectedBytes;
        SourceProvider = sourceProvider;
        ProviderRegistry = providerRegistry;
        _cachedFreeSpace = cachedFreeSpace is not null ? new FreeSpaceCache(cachedFreeSpace) : null;
    }

    /// <summary>Index of the step currently being validated. Set by <see cref="PipelineValidator"/> before each call.</summary>
    public int StepIndex { get; internal set; }

    /// <summary>Whether the source still exists at this point in the pipeline (post-conditions of earlier steps may set this to false).</summary>
    public bool SourceExists { get; set; }

    /// <summary>Whether at least one selected/included file is present (supplied externally).</summary>
    public bool HasSelectedIncludedInputs { get; set; }

    /// <summary>Approximate total bytes of selected files. Steps may reset this (e.g. InvertSelectionStep).</summary>
    public long SelectedBytes { get; set; }

    /// <summary>True after a step that inverts selection, so downstream steps skip the byte-based space check.</summary>
    public bool ByteEstimateUnknown { get; set; }

    /// <summary>Source provider for space-check resolution.</summary>
    public IFileSystemProvider? SourceProvider { get; }

    /// <summary>Provider registry for space-check resolution.</summary>
    public IPathResolver? ProviderRegistry { get; }

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

    /// <summary>Adds a non-blocking warning scoped to the current step.</summary>
    public void AddStepWarning(string code, string message)
    {
        _issues.Add(new PipelineValidationIssue(StepIndex, code, message, PipelineValidationSeverity.Warning));
    }

    /// <summary>
    /// Checks free space using the cached map and adds a step-scoped warning if insufficient.
    /// No-op when estimate is unknown, bytes ≤ 0, or context lacks providers/cache.
    /// </summary>
    public async Task AddFreeSpaceWarning(IHasFreeSpaceCheck step, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (ByteEstimateUnknown || SelectedBytes <= 0
            || _cachedFreeSpace is null
            || SourceProvider is null
            || ProviderRegistry is null) return;

        var result = await step.ValidateFreeSpace(SelectedBytes, SourceProvider, ProviderRegistry, _cachedFreeSpace, ct);
        if (result is null) return;
        if (result.IsViolation)
        {
            AddStepWarning("Step.InsufficientSpace", result.ShortMessage);
        }

        // Update free space cache
        _cachedFreeSpace.ReduceForProvider(ProviderRegistry.ResolveProvider(result.TargetRootPath)!, result.NeededBytes);
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
