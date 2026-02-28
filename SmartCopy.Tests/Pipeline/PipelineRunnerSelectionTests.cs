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

    /// <summary>
    /// Creates a single-provider fixture with several named files under /src,
    /// a /dest directory, and returns the nodes with their check states set.
    /// </summary>
    private static async Task<(MemoryFileSystemProvider Provider,
        FileSystemNode A, FileSystemNode B, FileSystemNode C,
        FileSystemNode D, FileSystemNode E)>
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

        var ct = CancellationToken.None;
        var a = await provider.GetNodeAsync("/src/a.txt", ct);
        var b = await provider.GetNodeAsync("/src/b.txt", ct);
        var c = await provider.GetNodeAsync("/src/c.txt", ct);
        var d = await provider.GetNodeAsync("/src/d.txt", ct);
        var e = await provider.GetNodeAsync("/src/e.txt", ct);

        return (provider, a, b, c, d, e);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // InvertSelection
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InvertSelection_then_Copy_CopiesOnlyNewlySelectedFiles()
    {
        var (provider, a, b, c, d, e) = await CreateFiveFileFixtureAsync();

        // A, B, C initially selected; D, E not selected
        a.CheckState = CheckState.Checked;
        b.CheckState = CheckState.Checked;
        c.CheckState = CheckState.Checked;

        FileSystemNode[] filterIncluded = [a, b, c, d, e];
        FileSystemNode[] selected = [a, b, c];

        var runner = new PipelineRunner(new TransformPipeline(
        [
            new InvertSelectionStep(),
            new CopyStep("/dest"),
        ]));

        await runner.ExecuteAsync(
            new PipelineJob
            {
                FilterIncludedFiles = filterIncluded,
                SelectedFiles       = selected,
                SourceProvider      = provider,
                TargetProvider      = provider,
                OverwriteMode       = OverwriteMode.Always,
                DeleteMode          = DeleteMode.Trash,
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

    // ─────────────────────────────────────────────────────────────────────────
    // SelectAll
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SelectAll_then_Copy_CopiesAllFilterIncludedFiles()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/src")
            .WithDirectory("/dest")
            .WithFile("/src/x.txt", "x"u8)
            .WithFile("/src/y.txt", "y"u8)
            .WithFile("/src/z.txt", "z"u8));

        var ct = CancellationToken.None;
        var x = await provider.GetNodeAsync("/src/x.txt", ct);
        var y = await provider.GetNodeAsync("/src/y.txt", ct);
        var z = await provider.GetNodeAsync("/src/z.txt", ct);

        // Only X is initially selected
        x.CheckState = CheckState.Checked;

        FileSystemNode[] filterIncluded = [x, y, z];
        FileSystemNode[] selected = [x];

        var runner = new PipelineRunner(new TransformPipeline(
        [
            new SelectAllStep(),
            new CopyStep("/dest"),
        ]));

        await runner.ExecuteAsync(
            new PipelineJob
            {
                FilterIncludedFiles = filterIncluded,
                SelectedFiles       = selected,
                SourceProvider      = provider,
                TargetProvider      = provider,
                OverwriteMode       = OverwriteMode.Always,
                DeleteMode          = DeleteMode.Trash,
            },
            progress: null,
            ct: CancellationToken.None);

        // All three files should be copied after SelectAll
        Assert.True(await provider.ExistsAsync("/dest/src/x.txt", ct));
        Assert.True(await provider.ExistsAsync("/dest/src/y.txt", ct));
        Assert.True(await provider.ExistsAsync("/dest/src/z.txt", ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ClearSelection
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ClearSelection_then_Copy_CopiesNothing()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/src")
            .WithDirectory("/dest")
            .WithFile("/src/p.txt", "p"u8)
            .WithFile("/src/q.txt", "q"u8)
            .WithFile("/src/r.txt", "r"u8));

        var ct = CancellationToken.None;
        var p = await provider.GetNodeAsync("/src/p.txt", ct);
        var q = await provider.GetNodeAsync("/src/q.txt", ct);
        var r = await provider.GetNodeAsync("/src/r.txt", ct);

        // All three initially selected
        p.CheckState = CheckState.Checked;
        q.CheckState = CheckState.Checked;
        r.CheckState = CheckState.Checked;

        FileSystemNode[] filterIncluded = [p, q, r];
        FileSystemNode[] selected = [p, q, r];

        var runner = new PipelineRunner(new TransformPipeline(
        [
            new ClearSelectionStep(),
            new CopyStep("/dest"),
        ]));

        await runner.ExecuteAsync(
            new PipelineJob
            {
                FilterIncludedFiles = filterIncluded,
                SelectedFiles       = selected,
                SourceProvider      = provider,
                TargetProvider      = provider,
                OverwriteMode       = OverwriteMode.Always,
                DeleteMode          = DeleteMode.Trash,
            },
            progress: null,
            ct: CancellationToken.None);

        // Nothing should be copied — ClearSelection empties the working set
        Assert.False(await provider.ExistsAsync("/dest/src/p.txt", ct));
        Assert.False(await provider.ExistsAsync("/dest/src/q.txt", ct));
        Assert.False(await provider.ExistsAsync("/dest/src/r.txt", ct));
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

        var ct = CancellationToken.None;
        var p = await sourceProvider.GetNodeAsync("/src/p.txt", ct);
        var q = await sourceProvider.GetNodeAsync("/src/q.txt", ct);
        var r = await sourceProvider.GetNodeAsync("/src/r.txt", ct);

        // None initially selected — SelectAll will select all
        FileSystemNode[] filterIncluded = [p, q, r];
        FileSystemNode[] selected = [];

        var runner = new PipelineRunner(new TransformPipeline(
        [
            new SelectAllStep(),
            new CopyStep("/dest"),
        ]));

        var plan = await runner.PreviewAsync(
            new PipelineJob
            {
                FilterIncludedFiles = filterIncluded,
                SelectedFiles       = selected,
                SourceProvider      = sourceProvider,
                TargetProvider      = targetProvider,
                OverwriteMode       = OverwriteMode.Always,
                DeleteMode          = DeleteMode.Trash,
            },
            ct);

        // Exactly 3 copy actions — one per filter-included file
        Assert.Equal(3, plan.Actions.Count);
        Assert.All(plan.Actions, a => Assert.Equal(SourcePathResult.Copied, a.SourcePathResult));
    }
}
