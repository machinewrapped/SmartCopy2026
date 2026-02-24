using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Tests.Pipeline;

public sealed class PipelineValidatorTests
{
    [Fact]
    public void PathOnlyPipeline_ReturnsNoExecutableBlockingIssue()
    {
        var result = PipelineValidator.Validate([new FlattenStep()]);

        Assert.False(result.CanRun);
        Assert.Contains(result.Issues, issue => issue.Code == "Pipeline.NoExecutableStep");
    }

    [Fact]
    public void MissingDestination_ReturnsStepScopedBlockingIssue()
    {
        var result = PipelineValidator.Validate([new CopyStep("")]);

        Assert.False(result.CanRun);
        var issue = Assert.Single(result.Issues, i => i.Code == "Step.MissingDestination");
        Assert.Equal(0, issue.StepIndex);
    }

    [Fact]
    public void ExecutablePipelineWithoutSelectedInputs_ReturnsBlockingIssue()
    {
        var result = PipelineValidator.Validate(
            [new CopyStep("/mem/out")],
            new PipelineValidationContext(HasSelectedIncludedInputs: false));

        Assert.False(result.CanRun);
        Assert.Contains(result.Issues, issue => issue.Code == "Pipeline.NoSelectedInputs" && issue.StepIndex is null);
    }

    [Fact]
    public void CopyThenMove_IsValid()
    {
        var result = PipelineValidator.Validate(
        [
            new CopyStep("/mem/backup"),
            new MoveStep("/mem/archive"),
        ]);

        Assert.True(result.CanRun);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void PathOnlyPipeline_DoesNotRequireSelectedInputs()
    {
        var result = PipelineValidator.Validate(
            [new FlattenStep()],
            new PipelineValidationContext(HasSelectedIncludedInputs: false));

        Assert.DoesNotContain(result.Issues, issue => issue.Code == "Pipeline.NoSelectedInputs");
    }

    [Fact]
    public void DeleteThenCopy_InvalidAtCopyStep_BecauseSourceNoLongerExists()
    {
        var result = PipelineValidator.Validate(
        [
            new DeleteStep(),
            new CopyStep("/mem/out"),
        ]);

        Assert.False(result.CanRun);
        Assert.Contains(result.Issues, issue => issue.Code == "Step.SourceMissing" && issue.StepIndex == 1);
    }

    [Fact]
    public void MoveThenDelete_InvalidAtDeleteStep_BecauseSourceNoLongerExists()
    {
        var result = PipelineValidator.Validate(
        [
            new MoveStep("/mem/out"),
            new DeleteStep(),
        ]);

        Assert.False(result.CanRun);
        Assert.Contains(result.Issues, issue => issue.Code == "Step.SourceMissing" && issue.StepIndex == 1);
    }

    [Fact]
    public void DeleteMustBeFinal_IsEnforced()
    {
        var result = PipelineValidator.Validate(
        [
            new CopyStep("/mem/out"),
            new DeleteStep(),
            new MoveStep("/mem/archive"),
        ]);

        Assert.False(result.CanRun);
        Assert.Contains(result.Issues, issue => issue.Code == "Step.DeleteMustBeFinal" && issue.StepIndex == 1);
    }
}
