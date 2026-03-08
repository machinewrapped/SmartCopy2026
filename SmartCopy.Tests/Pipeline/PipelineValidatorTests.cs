using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Tests.Pipeline;

public sealed class PipelineValidatorTests
{
    [Fact]
    public async Task PathOnlyPipeline_ReturnsNoExecutableBlockingIssue()
    {
        var result = await PipelineValidator.ValidateAsync([new FlattenStep()], new PipelineValidationContext());

        Assert.False(result.CanRun);
        Assert.Contains(result.Issues, issue => issue.Code == "Pipeline.NoExecutableStep");
    }

    [Fact]
    public async Task MissingDestination_ReturnsStepScopedBlockingIssue()
    {
        var result = await PipelineValidator.ValidateAsync([new CopyStep("")], new PipelineValidationContext());

        Assert.False(result.CanRun);
        var issue = Assert.Single(result.Issues, i => i.Code == "Step.MissingDestination");
        Assert.Equal(0, issue.StepIndex);
    }

    [Fact]
    public async Task ExecutablePipelineWithoutSelectedInputs_ReturnsBlockingIssue()
    {
        var result = await PipelineValidator.ValidateAsync(
            [new CopyStep("/mem/out")],
            new PipelineValidationContext(HasSelectedIncludedInputs: false));

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
        new PipelineValidationContext());

        Assert.True(result.CanRun);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public async Task PathOnlyPipeline_DoesNotRequireSelectedInputs()
    {
        var result = await PipelineValidator.ValidateAsync(
            [new FlattenStep()],
            new PipelineValidationContext(HasSelectedIncludedInputs: false));

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
        new PipelineValidationContext());

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
        new PipelineValidationContext());

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
        new PipelineValidationContext());

        Assert.True(result.CanRun);
        Assert.Empty(result.Issues);
    }
}
