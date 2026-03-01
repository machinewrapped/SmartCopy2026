using SmartCopy.Core.FileSystem;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Pipeline.Validation;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

public sealed class SelectionStepsTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<TransformContext> MakeContext(CheckState initialState = CheckState.Unchecked)
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithFile("/src/file.txt", "content"u8));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider);

        var node = root.FindNodeByPathSegments(["src", "file.txt"]);
        Assert.NotNull(node);
        node.CheckState = initialState;

        return new TransformContext
        {
            SourceNode = node,
            SourceProvider = provider,
            PathSegments = node.RelativePathSegments,
            CurrentExtension = ".txt",
        };
    }

    private static StepValidationContext MakeValidationContext(bool sourceExists = true) =>
        new(hasSelectedIncludedInputs: true, sourceExists: sourceExists);

    // -------------------------------------------------------------------------
    // SelectAllStep
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SelectAllStep_Preview_SetsCheckedAndReturnsNullDestination()
    {
        var step = new SelectAllStep();
        var ctx = await MakeContext(CheckState.Unchecked);

        var results = new List<TransformResult>();
        await foreach (var r in step.PreviewAsync(ctx, CancellationToken.None)) results.Add(r);
        var result = results.Single();

        Assert.True(result.IsSuccess);
        Assert.Equal(SourcePathResult.None, result.SourcePathResult);
        Assert.Null(result.DestinationPath);
        Assert.Equal(CheckState.Checked, ctx.SourceNode.CheckState);
    }

    [Fact]
    public async Task SelectAllStep_ApplyAsync_SetsChecked()
    {
        var step = new SelectAllStep();
        var ctx = await MakeContext(CheckState.Unchecked);

        var result = await step.ApplyAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
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
    public async Task ClearSelectionStep_Preview_SetsUncheckedAndReturnsNullDestination()
    {
        var step = new ClearSelectionStep();
        var ctx = await MakeContext(CheckState.Checked);

        var results = new List<TransformResult>();
        await foreach (var r in step.PreviewAsync(ctx, CancellationToken.None)) results.Add(r);
        var result = results.Single();

        Assert.True(result.IsSuccess);
        Assert.Equal(SourcePathResult.None, result.SourcePathResult);
        Assert.Null(result.DestinationPath);
        Assert.Equal(CheckState.Unchecked, ctx.SourceNode.CheckState);
    }

    [Fact]
    public async Task ClearSelectionStep_ApplyAsync_SetsUnchecked()
    {
        var step = new ClearSelectionStep();
        var ctx = await MakeContext(CheckState.Checked);

        var result = await step.ApplyAsync(ctx, CancellationToken.None);

        Assert.True(result.IsSuccess);
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
    public async Task InvertSelectionStep_Preview_TogglesCheckedAndReturnsNullDestination()
    {
        var step = new InvertSelectionStep();
        var ctx = await MakeContext(CheckState.Unchecked);

        var results = new List<TransformResult>();
        await foreach (var r in step.PreviewAsync(ctx, CancellationToken.None)) results.Add(r);
        var result = results.Single();

        Assert.True(result.IsSuccess);
        Assert.Equal(SourcePathResult.None, result.SourcePathResult);
        Assert.Null(result.DestinationPath);
        Assert.Equal(CheckState.Checked, ctx.SourceNode.CheckState);
    }

    [Fact]
    public async Task InvertSelectionStep_ApplyAsync_TogglesChecked()
    {
        var step = new InvertSelectionStep();
        var ctx = await MakeContext(CheckState.Checked);

        await step.ApplyAsync(ctx, CancellationToken.None);

        Assert.Equal(CheckState.Unchecked, ctx.SourceNode.CheckState);
    }

    [Fact]
    public async Task InvertSelectionStep_ApplyAsync_TogglesUnchecked()
    {
        var step = new InvertSelectionStep();
        var ctx = await MakeContext(CheckState.Unchecked);

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
