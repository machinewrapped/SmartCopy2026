using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Progress;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

public sealed class PauseTokenSourceTests
{
    // ── PauseTokenSource unit tests ──────────────────────────────────────────

    [Fact]
    public void IsPaused_StartsAsFalse()
    {
        using var pts = new PauseTokenSource();
        Assert.False(pts.IsPaused);
    }

    [Fact]
    public void Pause_SetsIsPausedTrue()
    {
        using var pts = new PauseTokenSource();
        pts.Pause();
        Assert.True(pts.IsPaused);
    }

    [Fact]
    public void Resume_AfterPause_SetsIsPausedFalse()
    {
        using var pts = new PauseTokenSource();
        pts.Pause();
        pts.Resume();
        Assert.False(pts.IsPaused);
    }

    [Fact]
    public void Pause_CalledTwice_IsIdempotent()
    {
        using var pts = new PauseTokenSource();
        pts.Pause();
        pts.Pause(); // must not throw
        Assert.True(pts.IsPaused);
    }

    [Fact]
    public void Resume_WhenNotPaused_IsIdempotent()
    {
        using var pts = new PauseTokenSource();
        pts.Resume(); // must not throw
        Assert.False(pts.IsPaused);
    }

    [Fact]
    public async Task WaitIfPausedAsync_NotPaused_CompletesImmediately()
    {
        using var pts = new PauseTokenSource();

        var task = pts.WaitIfPausedAsync(CancellationToken.None).AsTask();

        await task.WaitAsync(TimeSpan.FromMilliseconds(200));
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WaitIfPausedAsync_Paused_BlocksUntilResumed()
    {
        using var pts = new PauseTokenSource();
        pts.Pause();

        var waitTask = pts.WaitIfPausedAsync(CancellationToken.None).AsTask();

        // Should still be blocked after a brief wait
        await Task.Delay(30);
        Assert.False(waitTask.IsCompleted, "Should still be blocked while paused");

        pts.Resume();
        await waitTask.WaitAsync(TimeSpan.FromMilliseconds(500));
        Assert.True(waitTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task WaitIfPausedAsync_CancellationUnblocksWhilePaused()
    {
        using var pts = new PauseTokenSource();
        using var cts = new CancellationTokenSource();
        pts.Pause();

        var waitTask = pts.WaitIfPausedAsync(cts.Token).AsTask();
        await Task.Delay(20);
        Assert.False(waitTask.IsCompleted, "Should be blocked before cancellation");

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => waitTask.WaitAsync(TimeSpan.FromMilliseconds(200)));
    }

    // ── PipelineRunner integration tests ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_PauseAndResume_CompletesAllFiles()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/src")
            .WithDirectory("/dst")
            .WithFile("/src/a.txt", "a"u8)
            .WithFile("/src/b.txt", "b"u8)
            .WithFile("/src/c.txt", "c"u8));

        var root = await provider.BuildDirectoryTree();
        foreach (var seg in new[] { "a.txt", "b.txt", "c.txt" })
            root.FindNodeByPathSegments(["src", seg])!.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new CopyStep("/mem/dst")]));
        var job = new PipelineJob
        {
            RootNode = root,
            SourceProvider = provider,
            ProviderRegistry = provider.CreateRegistry(),
            OverwriteMode = OverwriteMode.Always,
            DeleteMode = DeleteMode.Trash,
        };

        using var pts = new PauseTokenSource();
        pts.Pause(); // pause before starting — runner will block on first result

        var runTask = Task.Run(() => runner.ExecuteAsync(
            job with { PauseToken = pts, CancellationToken = CancellationToken.None }));

        // Verify the runner is blocked (hasn't completed after brief wait)
        var winner = await Task.WhenAny(runTask, Task.Delay(50));
        Assert.NotSame(runTask, winner);

        // Resume and let it finish
        pts.Resume();
        var results = await runTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(3, results.Count(r => r.SourceNodeResult == SourceResult.Copied));
    }

    [Fact]
    public async Task ExecuteAsync_CancelWhilePaused_ThrowsOperationCanceledException()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/src")
            .WithDirectory("/dst")
            .WithFile("/src/a.txt", "a"u8)
            .WithFile("/src/b.txt", "b"u8));

        var root = await provider.BuildDirectoryTree();
        root.FindNodeByPathSegments(["src", "a.txt"])!.CheckState = CheckState.Checked;
        root.FindNodeByPathSegments(["src", "b.txt"])!.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new CopyStep("/mem/dst")]));
        var job = new PipelineJob
        {
            RootNode = root,
            SourceProvider = provider,
            ProviderRegistry = provider.CreateRegistry(),
            OverwriteMode = OverwriteMode.Always,
            DeleteMode = DeleteMode.Trash,
        };

        using var pts = new PauseTokenSource();
        using var cts = new CancellationTokenSource();
        pts.Pause();

        var runTask = Task.Run(() => runner.ExecuteAsync(
            job with { PauseToken = pts, CancellationToken = cts.Token }));

        // Verify blocked
        var winner = await Task.WhenAny(runTask, Task.Delay(50));
        Assert.NotSame(runTask, winner);

        // Cancelling while paused should unblock via OperationCanceledException
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runTask.WaitAsync(TimeSpan.FromSeconds(2)));
    }
}
