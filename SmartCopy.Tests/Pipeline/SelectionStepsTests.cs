using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Tests.Pipeline;

public sealed class SelectionStepsTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static TransformContext MakeContext(CheckState initialState = CheckState.Unchecked)
    {
        var provider = new MemoryFileSystemProvider();
        var node = new FileSystemNode
        {
            Name = "file.txt",
            FullPath = "/src/file.txt",
            RelativePathSegments = ["src", "file.txt"],
        };

        // Set the initial check state via the backing field by going through
        // the property so propagation logic runs (node has no parent here).
        node.CheckState = initialState;

        return new TransformContext
        {
            SourceNode = node,
            SourceProvider = provider,
            PathSegments = ["src", "file.txt"],
            CurrentExtension = ".txt",
        };
    }

    private static StepValidationContext MakeValidationContext(bool sourceExists = true) =>
        new(hasSelectedIncludedInputs: true, sourceExists: sourceExists);

    // -------------------------------------------------------------------------
    // SelectAllStep
    // -------------------------------------------------------------------------

    [Fact]
    public void SelectAllStep_Preview_ReturnsSuccess()
    {
        var step = new SelectAllStep();
        var ctx = MakeContext();

        var result = step.Preview(ctx);

        Assert.True(result.Success);
        Assert.Equal(StepKind.SelectAll, result.StepType);
        Assert.Equal("Mark as selected", result.Message);
    }

    [Fact]
    public async Task SelectAllStep_ApplyAsync_SetsChecked()
    {
        var step = new SelectAllStep();
        var ctx = MakeContext(CheckState.Unchecked);

        var result = await step.ApplyAsync(ctx, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(CheckState.Checked, ctx.SourceNode.CheckState);
    }

    [Fact]
    public void SelectAllStep_Validate_NoIssues()
    {
        var step = new SelectAllStep();
        var ctx = MakeValidationContext();

        step.Validate(ctx);

        Assert.Empty(ctx.Issues);
    }

    // -------------------------------------------------------------------------
    // ClearSelectionStep
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ClearSelectionStep_ApplyAsync_SetsUnchecked()
    {
        var step = new ClearSelectionStep();
        var ctx = MakeContext(CheckState.Checked);

        var result = await step.ApplyAsync(ctx, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(CheckState.Unchecked, ctx.SourceNode.CheckState);
    }

    [Fact]
    public void ClearSelectionStep_Validate_NoIssues()
    {
        var step = new ClearSelectionStep();
        var ctx = MakeValidationContext();

        step.Validate(ctx);

        Assert.Empty(ctx.Issues);
    }

    // -------------------------------------------------------------------------
    // InvertSelectionStep
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvertSelectionStep_ApplyAsync_TogglesChecked()
    {
        var step = new InvertSelectionStep();
        var ctx = MakeContext(CheckState.Checked);

        await step.ApplyAsync(ctx, CancellationToken.None);

        Assert.Equal(CheckState.Unchecked, ctx.SourceNode.CheckState);
    }

    [Fact]
    public async Task InvertSelectionStep_ApplyAsync_TogglesUnchecked()
    {
        var step = new InvertSelectionStep();
        var ctx = MakeContext(CheckState.Unchecked);

        await step.ApplyAsync(ctx, CancellationToken.None);

        Assert.Equal(CheckState.Checked, ctx.SourceNode.CheckState);
    }

    [Fact]
    public void InvertSelectionStep_Validate_SetsSourceExistsTrue()
    {
        var step = new InvertSelectionStep();
        // Start with SourceExists = false (as if a prior Delete step ran)
        var ctx = MakeValidationContext(sourceExists: false);

        step.Validate(ctx);

        Assert.True(ctx.SourceExists);
        Assert.Empty(ctx.Issues);
    }
}
