using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Tests.Pipeline;

public sealed class PipelineValidatorTests
{
    private static PipelineValidationContext MakeContext(
        IFileSystemProvider? sourceProvider = null,
        FileSystemProviderRegistry? registry = null,
        FreeSpaceCache? freeSpaceCache = null,
        bool hasSelectedInputs = true, 
        long totalSelectedBytes = 1)
    {
        return new PipelineValidationContext(
            sourceProvider ?? new MemoryFileSystemProvider(),
            registry ?? new FileSystemProviderRegistry(),
            freeSpaceCache ?? new FreeSpaceCache(),
            hasSelectedInputs,
            totalSelectedBytes
        );
    }

    [Fact]
    public async Task PathOnlyPipeline_ReturnsNoExecutableBlockingIssue()
    {
        var result = await PipelineValidator.ValidateAsync([new FlattenStep()], 
            MakeContext());

        Assert.False(result.CanRun);
        Assert.Contains(result.Issues, issue => issue.Code == "Pipeline.NoExecutableStep");
    }

    [Fact]
    public async Task MissingDestination_ReturnsStepScopedBlockingIssue()
    {
        IReadOnlyList<IPipelineStep> steps = [new CopyStep("")];
        PipelineValidationContext context = MakeContext();
        var result = await PipelineValidator.ValidateAsync(steps, context);

        Assert.False(result.CanRun);
        var issue = Assert.Single(result.Issues, i => i.Code == "Step.MissingDestination");
        Assert.Equal(0, issue.StepIndex);
    }

    [Fact]
    public async Task ExecutablePipelineWithoutSelectedInputs_ReturnsBlockingIssue()
    {
        var result = await PipelineValidator.ValidateAsync(
            [new CopyStep("/mem/out")],
            MakeContext(hasSelectedInputs: false));

        Assert.False(result.CanRun);
        Assert.Contains(result.Issues, issue => issue.Code == "Pipeline.NoSelectedInputs" && issue.StepIndex is null);
    }

    [Fact]
    public async Task CopyThenMove_IsValid()
    {
        var result = await PipelineValidator.ValidateAsync(
        [
            new CopyStep("/mem/backup"),
            new MoveStep("/mem/archive"),
        ],
        MakeContext());

        Assert.True(result.CanRun);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task PathOnlyPipeline_DoesNotRequireSelectedInputs()
    {
        var result = await PipelineValidator.ValidateAsync(
            [new FlattenStep()],
            MakeContext(hasSelectedInputs: false));

        Assert.DoesNotContain(result.Issues, issue => issue.Code == "Pipeline.NoSelectedInputs");
    }

    [Fact]
    public async Task DeleteThenCopy_InvalidAtCopyStep_BecauseSourceNoLongerExists()
    {
        var result = await PipelineValidator.ValidateAsync(
        [
            new DeleteStep(),
            new CopyStep("/mem/out"),
        ],
        MakeContext());

        Assert.False(result.CanRun);
        Assert.Contains(result.Issues, issue => issue.Code == "Step.SourceMissing" && issue.StepIndex == 1);
    }

    [Fact]
    public async Task MoveThenDelete_InvalidAtDeleteStep_BecauseSourceNoLongerExists()
    {
        var result = await PipelineValidator.ValidateAsync(
        [
            new MoveStep("/mem/out"),
            new DeleteStep(),
        ],
        MakeContext());

        Assert.False(result.CanRun);
        Assert.Contains(result.Issues, issue => issue.Code == "Step.SourceMissing" && issue.StepIndex == 1);
    }

    [Fact]
    public async Task InvertSelection_AfterDelete_ResetsSourceExists()
    {
        // InvertSelection re-sets SourceExists=true, so Copy after it should be valid.
        var result = await PipelineValidator.ValidateAsync(
        [
            new DeleteStep(),
            new InvertSelectionStep(),
            new CopyStep("/mem/out"),
        ],
        MakeContext());

        Assert.True(result.CanRun);
        Assert.Empty(result.Issues);
    }
}
