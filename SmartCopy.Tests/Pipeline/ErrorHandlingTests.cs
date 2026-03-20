using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Trash;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

/// <summary>
/// Verifies that I/O failures during execution (locked files, permission errors) are caught
/// gracefully: the failing node yields IsSuccess=false with an ErrorMessage, and remaining
/// nodes continue to be processed.
/// </summary>
public sealed class ErrorHandlingTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Shared test context
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class TestContext(DirectoryNode root, IFileSystemProvider source, IFileSystemProvider? target = null) : IStepContext
    {
        private readonly Dictionary<DirectoryTreeNode, PipelineContext> _ctxCache = new();
        private readonly HashSet<DirectoryTreeNode> _failed = new();

        public DirectoryNode RootNode { get; } = root;
        public IFileSystemProvider SourceProvider { get; } = source;
        public bool ShowHiddenFiles { get; }
        public bool AllowDeleteReadOnly { get; }
        public ITrashService TrashService { get; } = new NullTrashService();

        public FileSystemProviderRegistry ProviderRegistry { get; } = BuildRegistry(source, target);

        private static FileSystemProviderRegistry BuildRegistry(IFileSystemProvider source, IFileSystemProvider? target)
        {
            var reg = new FileSystemProviderRegistry();
            reg.Register(source);
            if (target != null && target != source)
                reg.Register(target);
            return reg;
        }

        public PipelineContext GetNodeContext(DirectoryTreeNode node)
        {
            if (!_ctxCache.TryGetValue(node, out var ctx))
            {
                ctx = new PipelineContext
                {
                    SourceNode = node,
                    SourceProvider = SourceProvider,
                    ProviderRegistry = ProviderRegistry,
                    PathSegments = node.RelativePathSegments.Length > 0 ? node.RelativePathSegments : [node.Name],
                    CurrentExtension = Path.GetExtension(node.Name).TrimStart('.'),
                    VirtualCheckState = node.CheckState,
                };
                _ctxCache[node] = ctx;
            }
            return ctx;
        }

        public bool IsNodeFailed(DirectoryTreeNode node) => _failed.Contains(node);
        public void MarkFailed(DirectoryTreeNode node) => _failed.Add(node);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CopyStep
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When OpenReadAsync throws IOException for the first file, CopyStep yields a failure
    /// result for that file and continues to successfully copy the second file.
    /// </summary>
    [Fact]
    public async Task CopyStep_LockedSource_YieldsFailureAndContinues()
    {
        var inner = MemoryFileSystemFixtures.Create(s => s
            .WithFile("/src/a.txt", "aaa"u8)
            .WithFile("/src/b.txt", "bbb"u8)
            .WithDirectory("/dest"));

        var source = new FaultingProvider(inner) { FaultOnOpen = path => path.EndsWith("a.txt") };

        var root = await inner.BuildDirectoryTree("/src");
        foreach (var f in root.Files) f.CheckState = CheckState.Checked;

        var ctx = new TestContext(root, source);
        var step = new CopyStep("/mem/dest");

        var results = new List<TransformResult>();
        await foreach (var r in step.ApplyAsync(ctx, CancellationToken.None))
            results.Add(r);

        Assert.Equal(2, results.Count);

        var failed = results.Single(r => !r.IsSuccess);
        Assert.Equal("a.txt", failed.SourceNode.Name);
        Assert.Equal(SourceResult.Skipped, failed.SourceNodeResult);
        Assert.NotNull(failed.ErrorMessage);
        Assert.True(ctx.IsNodeFailed(failed.SourceNode));

        var succeeded = results.Single(r => r.IsSuccess);
        Assert.Equal("b.txt", succeeded.SourceNode.Name);
        Assert.Equal(SourceResult.Copied, succeeded.SourceNodeResult);
        Assert.True(await inner.ExistsAsync("/dest/b.txt", CancellationToken.None));
        Assert.False(await inner.ExistsAsync("/dest/a.txt", CancellationToken.None));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MoveStep — atomic path
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When MoveAsync throws IOException for the first file (atomic same-volume path),
    /// MoveStep yields a failure for that file and continues to move the second file.
    /// </summary>
    [Fact]
    public async Task MoveStep_AtomicMove_LockedFile_YieldsFailureAndContinues()
    {
        var inner = MemoryFileSystemFixtures.Create(s => s
            .WithFile("/src/a.txt", "aaa"u8)
            .WithFile("/src/b.txt", "bbb"u8), volumeId: "VOL");

        var source = new FaultingProvider(inner) { FaultOnMove = path => path.EndsWith("a.txt") };
        // Same volumeId → sameVolume=true; CanAtomicMove=true → atomic path taken.

        var root = await inner.BuildDirectoryTree("/src");
        foreach (var f in root.Files) f.CheckState = CheckState.Checked;

        var ctx = new TestContext(root, source);
        var step = new MoveStep("/mem/dest");

        var results = new List<TransformResult>();
        await foreach (var r in step.ApplyAsync(ctx, CancellationToken.None))
            results.Add(r);

        Assert.Equal(2, results.Count);

        var failed = results.Single(r => !r.IsSuccess);
        Assert.Equal("a.txt", failed.SourceNode.Name);
        Assert.NotNull(failed.ErrorMessage);
        Assert.True(ctx.IsNodeFailed(failed.SourceNode));

        var moved = results.Single(r => r.IsSuccess);
        Assert.Equal("b.txt", moved.SourceNode.Name);
        Assert.Equal(SourceResult.Moved, moved.SourceNodeResult);
        Assert.True(await inner.ExistsAsync("/dest/b.txt", CancellationToken.None));
        Assert.False(await inner.ExistsAsync("/src/b.txt", CancellationToken.None));
        // Locked file remains at source
        Assert.True(await inner.ExistsAsync("/src/a.txt", CancellationToken.None));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MoveStep — cross-volume copy+delete fallback
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cross-volume move where copy succeeds but DeleteAsync throws: result is IsSuccess=false
    /// with an ErrorMessage that mentions the delete failure. The copied file is left at
    /// the destination (data safe) and the source is also left (not deleted).
    /// </summary>
    [Fact]
    public async Task MoveStep_CrossVolume_DeleteFailsAfterCopy_YieldsFailureWithMessage()
    {
        var innerSource = MemoryFileSystemFixtures.Create(s => s
            .WithFile("/src/file.txt", "content"u8), volumeId: "VOL1");

        // Fault on DeleteAsync so the copy succeeds but the source isn't cleaned up.
        var faultingSource = new FaultingProvider(innerSource) { FaultOnDelete = _ => true };

        var target = MemoryFileSystemFixtures.Create(t => t.WithDirectory("dest"),
            customRootPath: "/target", volumeId: "VOL2");

        var root = await innerSource.BuildDirectoryTree("/src");
        root.Files[0].CheckState = CheckState.Checked;

        var ctx = new TestContext(root, faultingSource, target);
        var step = new MoveStep("/target/dest");

        var results = new List<TransformResult>();
        await foreach (var r in step.ApplyAsync(ctx, CancellationToken.None))
            results.Add(r);

        Assert.Single(results);
        var result = results[0];
        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("source could not be deleted", result.ErrorMessage);
        // File was copied to destination
        Assert.True(await target.ExistsAsync("/dest/file.txt", CancellationToken.None));
        // Source not deleted (delete failed)
        Assert.True(await innerSource.ExistsAsync("/src/file.txt", CancellationToken.None));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DeleteStep
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When DeleteAsync throws IOException for the first file, DeleteStep yields a failure
    /// result for that file and continues to delete the second file.
    /// </summary>
    [Fact]
    public async Task DeleteStep_LockedFile_YieldsFailureAndContinues()
    {
        var inner = MemoryFileSystemFixtures.Create(s => s
            .WithFile("/src/a.txt", "aaa"u8)
            .WithFile("/src/b.txt", "bbb"u8)
            .WithFile("/src/c.txt", "ccc"u8));  // unchecked — keeps root Indeterminate

        var source = new FaultingProvider(inner) { FaultOnDelete = path => path.EndsWith("a.txt") };

        var root = await inner.BuildDirectoryTree("/src");
        // Check only a.txt and b.txt; leave c.txt unchecked so root stays Indeterminate
        // and DeleteStep uses the per-node path instead of atomically deleting the root.
        root.Files.Single(f => f.Name == "a.txt").CheckState = CheckState.Checked;
        root.Files.Single(f => f.Name == "b.txt").CheckState = CheckState.Checked;

        var ctx = new TestContext(root, source);
        var step = new DeleteStep(DeleteMode.Permanent);

        // DeleteStep requires preview first.
        var previewResults = new List<TransformResult>();
        await foreach (var r in step.PreviewAsync(ctx, CancellationToken.None))
            previewResults.Add(r);

        var results = new List<TransformResult>();
        await foreach (var r in step.ApplyAsync(ctx, CancellationToken.None))
            results.Add(r);

        Assert.Equal(2, results.Count);

        var failed = results.Single(r => !r.IsSuccess);
        Assert.Equal("a.txt", failed.SourceNode.Name);
        Assert.Equal(SourceResult.Skipped, failed.SourceNodeResult);
        Assert.NotNull(failed.ErrorMessage);

        var deleted = results.Single(r => r.IsSuccess);
        Assert.Equal("b.txt", deleted.SourceNode.Name);
        Assert.Equal(SourceResult.Deleted, deleted.SourceNodeResult);
        Assert.False(await inner.ExistsAsync("/src/b.txt", CancellationToken.None));
        Assert.True(await inner.ExistsAsync("/src/a.txt", CancellationToken.None));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MoveStep — atomic directory move fallback
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the atomic directory-level MoveAsync throws (e.g. a locked file inside the
    /// directory prevents the OS rename), MoveStep falls back to a piecewise recursive
    /// walk and still moves all files individually.
    /// </summary>
    [Fact]
    public async Task MoveStep_AtomicDirectoryMove_FailsAndFallsBack()
    {
        var inner = MemoryFileSystemFixtures.Create(s => s
            .WithFile("/src/subdir/a.txt", "aaa"u8)
            .WithFile("/src/subdir/b.txt", "bbb"u8), volumeId: "VOL");

        var root = await inner.BuildDirectoryTree("/src");
        var subdir = root.Children.Single() as DirectoryNode;
        Assert.NotNull(subdir);
        foreach (var f in subdir.Files)
            f.CheckState = CheckState.Checked;

        // Fault only the directory-level atomic move, not individual file moves.
        var source = new FaultingProvider(inner) { FaultOnMove = path => path == subdir.FullPath };

        var ctx = new TestContext(root, source);
        var step = new MoveStep("/mem/dest");

        var results = new List<TransformResult>();
        await foreach (var r in step.ApplyAsync(ctx, CancellationToken.None))
            results.Add(r);

        // Both files moved individually via piecewise fallback — no failures.
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.All(results, r => Assert.Equal(SourceResult.Moved, r.SourceNodeResult));
        Assert.True(await inner.ExistsAsync("/mem/dest/subdir/a.txt", CancellationToken.None));
        Assert.True(await inner.ExistsAsync("/mem/dest/subdir/b.txt", CancellationToken.None));
        Assert.False(await inner.ExistsAsync("/src/subdir/a.txt", CancellationToken.None));
        Assert.False(await inner.ExistsAsync("/src/subdir/b.txt", CancellationToken.None));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DeleteStep — atomic root delete fallback
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// When the atomic root DeleteAsync throws (e.g. a locked file inside prevents the
    /// whole-directory delete), DeleteStep falls back to deleting each selected file
    /// individually so the rest of the tree is still cleaned up.
    /// </summary>
    [Fact]
    public async Task DeleteStep_AtomicRootDelete_FailsAndFallsBack()
    {
        var inner = MemoryFileSystemFixtures.Create(s => s
            .WithFile("/src/a.txt", "aaa"u8)
            .WithFile("/src/b.txt", "bbb"u8));

        var root = await inner.BuildDirectoryTree("/src");
        foreach (var f in root.Files) f.CheckState = CheckState.Checked;
        // Root is now Checked → DeleteStep takes the atomic-root path first.

        // Fault only the root directory delete, not individual file deletes.
        var source = new FaultingProvider(inner) { FaultOnDelete = path => path == root.FullPath };
        var ctx = new TestContext(root, source);
        var step = new DeleteStep(DeleteMode.Permanent);

        var previewResults = new List<TransformResult>();
        await foreach (var r in step.PreviewAsync(ctx, CancellationToken.None))
            previewResults.Add(r);

        var results = new List<TransformResult>();
        await foreach (var r in step.ApplyAsync(ctx, CancellationToken.None))
            results.Add(r);

        // Both files deleted individually via piecewise fallback — no failures.
        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.IsSuccess));
        Assert.All(results, r => Assert.Equal(SourceResult.Deleted, r.SourceNodeResult));
        Assert.False(await inner.ExistsAsync("/src/a.txt", CancellationToken.None));
        Assert.False(await inner.ExistsAsync("/src/b.txt", CancellationToken.None));
    }
}
