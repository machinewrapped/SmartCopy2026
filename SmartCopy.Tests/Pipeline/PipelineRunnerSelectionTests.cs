using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

public sealed class PipelineRunnerSelectionTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<(MemoryFileSystemProvider Provider, DirectoryTreeNode Root,
        DirectoryTreeNode A, DirectoryTreeNode B, DirectoryTreeNode C)>
        CreateThreeFileFixtureAsync(string name1, string name2, string name3)
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/src")
            .WithFile($"/src/{name1}.txt", "a"u8)
            .WithFile($"/src/{name2}.txt", "b"u8)
            .WithFile($"/src/{name3}.txt", "c"u8));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider);
        var a = root.FindNodeByPathSegments(["src", $"{name1}.txt"]);
        var b = root.FindNodeByPathSegments(["src", $"{name2}.txt"]);
        var c = root.FindNodeByPathSegments(["src", $"{name3}.txt"]);
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotNull(c);

        return (provider, root, a, b, c);
    }

    private static async Task<(MemoryFileSystemProvider Provider, DirectoryTreeNode Root,
        DirectoryTreeNode A, DirectoryTreeNode B, DirectoryTreeNode C,
        DirectoryTreeNode D, DirectoryTreeNode E)>
        CreateFiveFileFixtureAsync()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/src")
            .WithDirectory("/dest")
            .WithFile("/src/a.txt", "a"u8)
            .WithFile("/src/b.txt", "b"u8)
            .WithFile("/src/c.txt", "c"u8)
            .WithFile("/src/d.txt", "d"u8)
            .WithFile("/src/e.txt", "e"u8));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider);
        var a = root.FindNodeByPathSegments(["src", "a.txt"]);
        var b = root.FindNodeByPathSegments(["src", "b.txt"]);
        var c = root.FindNodeByPathSegments(["src", "c.txt"]);
        var d = root.FindNodeByPathSegments(["src", "d.txt"]);
        var e = root.FindNodeByPathSegments(["src", "e.txt"]);
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotNull(c);
        Assert.NotNull(d);
        Assert.NotNull(e);

        return (provider, root, a, b, c, d, e);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // InvertSelection
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvertSelection_then_Copy_CopiesOnlyNewlySelectedFiles()
    {
        var (provider, root, a, b, c, d, e) = await CreateFiveFileFixtureAsync();

        // A, B, C initially selected; D, E not selected
        a.CheckState = CheckState.Checked;
        b.CheckState = CheckState.Checked;
        c.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline(
        [
            new InvertSelectionStep(),
            new CopyStep("/dest"),
        ]));

        await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = root,
                SourceProvider = provider,
                TargetProvider = provider,
                OverwriteMode  = OverwriteMode.Always,
                DeleteMode     = DeleteMode.Trash,
            },
            progress: null,
            ct: CancellationToken.None);

        // D and E should have been copied (they were unchecked → inverted to checked)
        Assert.True(await provider.ExistsAsync("/dest/src/d.txt", CancellationToken.None));
        Assert.True(await provider.ExistsAsync("/dest/src/e.txt", CancellationToken.None));

        // A, B, C should NOT have been copied (they were checked → inverted to unchecked)
        Assert.False(await provider.ExistsAsync("/dest/src/a.txt", CancellationToken.None));
        Assert.False(await provider.ExistsAsync("/dest/src/b.txt", CancellationToken.None));
        Assert.False(await provider.ExistsAsync("/dest/src/c.txt", CancellationToken.None));
    }

    [Fact]
    public async Task InvertSelection_Preview_then_Execute_CopiesInvertedFiles()
    {
        // Regression: PreviewAsync must not mutate real CheckState so that
        // ExecuteAsync sees the correct (un-inverted) state and inverts it once.
        var (provider, root, a, b, c, d, e) = await CreateFiveFileFixtureAsync();

        // A, B, C initially selected; D, E not selected
        a.CheckState = CheckState.Checked;
        b.CheckState = CheckState.Checked;
        c.CheckState = CheckState.Checked;

        var job = new PipelineJob
        {
            RootNode       = root,
            SourceProvider = provider,
            TargetProvider = provider,
            OverwriteMode  = OverwriteMode.Always,
            DeleteMode     = DeleteMode.Trash,
        };

        var runner = new PipelineRunner(new TransformPipeline(
        [
            new InvertSelectionStep(),
            new CopyStep("/dest"),
        ]));

        // Preview first — this must NOT flip real CheckState
        await runner.PreviewAsync(job, ct: CancellationToken.None);

        // Execute — inversion happens exactly once on the real tree
        await runner.ExecuteAsync(job, progress: null, ct: CancellationToken.None);

        // D and E should have been copied (they were unchecked → inverted to checked)
        Assert.True(await provider.ExistsAsync("/dest/src/d.txt", CancellationToken.None));
        Assert.True(await provider.ExistsAsync("/dest/src/e.txt", CancellationToken.None));

        // A, B, C should NOT have been copied (they were checked → inverted to unchecked)
        Assert.False(await provider.ExistsAsync("/dest/src/a.txt", CancellationToken.None));
        Assert.False(await provider.ExistsAsync("/dest/src/b.txt", CancellationToken.None));
        Assert.False(await provider.ExistsAsync("/dest/src/c.txt", CancellationToken.None));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SelectAll
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SelectAll_then_Copy_CopiesAllFilterIncludedFiles()
    {
        var (provider, root, x, y, z) = await CreateThreeFileFixtureAsync("x", "y", "z");

        // Only X is initially selected
        x.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline(
        [
            new SelectAllStep(),
            new CopyStep("/dest"),
        ]));

        await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = root,
                SourceProvider = provider,
                TargetProvider = provider,
                OverwriteMode  = OverwriteMode.Always,
                DeleteMode     = DeleteMode.Trash,
            },
            progress: null,
            ct: CancellationToken.None);

        // All three files should be copied after SelectAll
        Assert.True(await provider.ExistsAsync("/dest/src/x.txt", CancellationToken.None));
        Assert.True(await provider.ExistsAsync("/dest/src/y.txt", CancellationToken.None));
        Assert.True(await provider.ExistsAsync("/dest/src/z.txt", CancellationToken.None));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ClearSelection
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearSelection_then_Copy_CopiesNothing()
    {
        var (provider, root, p, q, r) = await CreateThreeFileFixtureAsync("p", "q", "r");

        // All three initially selected
        p.CheckState = CheckState.Checked;
        q.CheckState = CheckState.Checked;
        r.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline(
        [
            new ClearSelectionStep(),
            new CopyStep("/dest"),
        ]));

        await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = root,
                SourceProvider = provider,
                TargetProvider = provider,
                OverwriteMode  = OverwriteMode.Always,
                DeleteMode     = DeleteMode.Trash,
            },
            progress: null,
            ct: CancellationToken.None);

        // Nothing should be copied — ClearSelection empties the working set
        Assert.False(await provider.ExistsAsync("/dest/src/p.txt", CancellationToken.None));
        Assert.False(await provider.ExistsAsync("/dest/src/q.txt", CancellationToken.None));
        Assert.False(await provider.ExistsAsync("/dest/src/r.txt", CancellationToken.None));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Preview — no spurious selection-step entries in OperationPlan
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SelectAll_Preview_ShowsOnlyCopyActionsNotSelectionActions()
    {
        var (sourceProvider, targetProvider) = MemoryFileSystemFixtures.CreatePair(
            src => src
                .WithDirectory("/src")
                .WithFile("/src/p.txt", "p"u8)
                .WithFile("/src/q.txt", "q"u8)
                .WithFile("/src/r.txt", "r"u8),
            tgt => tgt.WithDirectory("/dest"));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(sourceProvider);
        var p = root.FindNodeByPathSegments(["src", "p.txt"]);
        var q = root.FindNodeByPathSegments(["src", "q.txt"]);
        var r = root.FindNodeByPathSegments(["src", "r.txt"]);
        Assert.NotNull(p);
        Assert.NotNull(q);
        Assert.NotNull(r);

        // None initially selected — SelectAll will select all
        var runner = new PipelineRunner(new TransformPipeline(
        [
            new SelectAllStep(),
            new CopyStep("/dest"),
        ]));

        var plan = await runner.PreviewAsync(
            new PipelineJob
            {
                RootNode       = root,
                SourceProvider = sourceProvider,
                TargetProvider = targetProvider,
                OverwriteMode  = OverwriteMode.Always,
                DeleteMode     = DeleteMode.Trash,
            },
            ct: CancellationToken.None);

        // Exactly 3 copy actions — one per filter-included file
        Assert.Equal(3, plan.Actions.Count);
        Assert.All(plan.Actions, a => Assert.Equal(SourceResult.Copied, a.SourceResult));
    }
}
