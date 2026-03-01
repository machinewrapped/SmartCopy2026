using System.Collections.Generic;
using System.IO;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Pipeline.Validation;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

public sealed class SelectionStepsTests
{
    // -------------------------------------------------------------------------
    // Test infrastructure
    // -------------------------------------------------------------------------

    /// <summary>
    /// Minimal IStepContext for unit-testing individual steps.
    /// </summary>
    private sealed class TestStepContext : IStepContext
    {
        private readonly Dictionary<DirectoryTreeNode, TransformContext> _contexts = new();
        private readonly HashSet<DirectoryTreeNode> _failed = new();

        public DirectoryTreeNode RootNode { get; }
        public IFileSystemProvider SourceProvider { get; }
        public IFileSystemProvider? TargetProvider => null;
        public OverwriteMode OverwriteMode => OverwriteMode.Always;
        public DeleteMode DeleteMode => DeleteMode.Trash;

        public TestStepContext(DirectoryTreeNode root, IFileSystemProvider provider)
        {
            RootNode = root;
            SourceProvider = provider;
        }

        public TransformContext GetNodeContext(DirectoryTreeNode node)
        {
            if (!_contexts.TryGetValue(node, out var ctx))
            {
                ctx = new TransformContext
                {
                    SourceNode = node,
                    SourceProvider = SourceProvider,
                    PathSegments = node.RelativePathSegments.Length > 0
                        ? node.RelativePathSegments
                        : [node.Name],
                    CurrentExtension = Path.GetExtension(node.Name).TrimStart('.'),
                    OverwriteMode = OverwriteMode,
                    DeleteMode = DeleteMode,
                };
                _contexts[node] = ctx;
            }
            return ctx;
        }

        public bool IsNodeFailed(DirectoryTreeNode node) => _failed.Contains(node);
        public void MarkFailed(DirectoryTreeNode node) => _failed.Add(node);
    }

    /// <summary>
    /// Builds a minimal tree rooted at /src with a single file.txt.
    /// </summary>
    private static async Task<(DirectoryTreeNode Root, DirectoryTreeNode File, IFileSystemProvider Provider)>
        MakeTree(CheckState initialState = CheckState.Unchecked)
    {
        var provider = MemoryFileSystemFixtures.Create(f => f.WithFile("/src/file.txt", "content"u8));
        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider, "/src");
        var file = root.Files.Single(f => f.Name == "file.txt");
        if (initialState != CheckState.Unchecked)
            file.CheckState = initialState;
        return (root, file, provider);
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
        var (root, file, provider) = await MakeTree(CheckState.Unchecked);
        var ctx = new TestStepContext(root, provider);

        var results = new List<TransformResult>();
        await foreach (var r in step.PreviewAsync(ctx, CancellationToken.None)) results.Add(r);

        var result = results.Single();
        Assert.True(result.IsSuccess);
        Assert.Equal(SourcePathResult.None, result.SourcePathResult);
        Assert.Null(result.DestinationPath);
        Assert.Equal(CheckState.Checked, file.CheckState);
    }

    [Fact]
    public async Task SelectAllStep_ApplyAsync_SetsChecked()
    {
        var step = new SelectAllStep();
        var (root, file, provider) = await MakeTree(CheckState.Unchecked);
        var ctx = new TestStepContext(root, provider);

        var results = new List<TransformResult>();
        await foreach (var r in step.ApplyAsync(ctx, CancellationToken.None)) results.Add(r);

        Assert.True(results.Single().IsSuccess);
        Assert.Equal(CheckState.Checked, file.CheckState);
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
        var (root, file, provider) = await MakeTree(CheckState.Checked);
        var ctx = new TestStepContext(root, provider);

        var results = new List<TransformResult>();
        await foreach (var r in step.PreviewAsync(ctx, CancellationToken.None)) results.Add(r);

        var result = results.Single();
        Assert.True(result.IsSuccess);
        Assert.Equal(SourcePathResult.None, result.SourcePathResult);
        Assert.Null(result.DestinationPath);
        Assert.Equal(CheckState.Unchecked, file.CheckState);
    }

    [Fact]
    public async Task ClearSelectionStep_ApplyAsync_SetsUnchecked()
    {
        var step = new ClearSelectionStep();
        var (root, file, provider) = await MakeTree(CheckState.Checked);
        var ctx = new TestStepContext(root, provider);

        var results = new List<TransformResult>();
        await foreach (var r in step.ApplyAsync(ctx, CancellationToken.None)) results.Add(r);

        Assert.True(results.Single().IsSuccess);
        Assert.Equal(CheckState.Unchecked, file.CheckState);
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
        var (root, file, provider) = await MakeTree(CheckState.Unchecked);
        var ctx = new TestStepContext(root, provider);

        var results = new List<TransformResult>();
        await foreach (var r in step.PreviewAsync(ctx, CancellationToken.None)) results.Add(r);

        var result = results.Single();
        Assert.True(result.IsSuccess);
        Assert.Equal(SourcePathResult.None, result.SourcePathResult);
        Assert.Null(result.DestinationPath);
        Assert.Equal(CheckState.Checked, file.CheckState);
    }

    [Fact]
    public async Task InvertSelectionStep_ApplyAsync_TogglesChecked()
    {
        var step = new InvertSelectionStep();
        var (root, file, provider) = await MakeTree(CheckState.Checked);
        var ctx = new TestStepContext(root, provider);

        await foreach (var _ in step.ApplyAsync(ctx, CancellationToken.None)) { }

        Assert.Equal(CheckState.Unchecked, file.CheckState);
    }

    [Fact]
    public async Task InvertSelectionStep_ApplyAsync_TogglesUnchecked()
    {
        var step = new InvertSelectionStep();
        var (root, file, provider) = await MakeTree(CheckState.Unchecked);
        var ctx = new TestStepContext(root, provider);

        await foreach (var _ in step.ApplyAsync(ctx, CancellationToken.None)) { }

        Assert.Equal(CheckState.Checked, file.CheckState);
    }

    [Fact]
    public void InvertSelectionStep_Validate_SetsSourceExistsTrue()
    {
        var step = new InvertSelectionStep();
        var ctx = MakeValidationContext(sourceExists: false);

        step.Validate(ctx);

        Assert.True(ctx.SourceExists);
        Assert.Empty(ctx.Issues);
    }
}
