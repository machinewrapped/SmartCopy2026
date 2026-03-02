using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

/// <summary>
/// Tests for <see cref="MoveStep.ApplyAsync"/> fallback paths:
/// cross-provider (copy+delete), directory failure, same-provider atomic, and CanAtomicMove=false.
/// </summary>
public sealed class MoveStepFallbackTests
{
    // -------------------------------------------------------------------------
    // Minimal IStepContext that supports a configurable TargetProvider.
    // -------------------------------------------------------------------------

    private sealed class MoveTestContext : IStepContext
    {
        private readonly Dictionary<DirectoryTreeNode, PipelineContext> _contexts = new();
        private readonly HashSet<DirectoryTreeNode> _failed = new();

        public DirectoryTreeNode RootNode { get; }
        public IFileSystemProvider SourceProvider { get; }
        public IFileSystemProvider? TargetProvider { get; }
        public OverwriteMode OverwriteMode => OverwriteMode.Always;
        public DeleteMode DeleteMode => DeleteMode.Permanent;

        public MoveTestContext(DirectoryTreeNode root, IFileSystemProvider source, IFileSystemProvider? target = null)
        {
            RootNode = root;
            SourceProvider = source;
            TargetProvider = target ?? source;
        }

        public PipelineContext GetNodeContext(DirectoryTreeNode node)
        {
            if (!_contexts.TryGetValue(node, out var ctx))
            {
                ctx = new PipelineContext
                {
                    SourceNode = node,
                    SourceProvider = SourceProvider,
                    PathSegments = node.RelativePathSegments.Length > 0
                        ? node.RelativePathSegments
                        : [node.Name],
                    CurrentExtension = Path.GetExtension(node.Name).TrimStart('.'),
                    OverwriteMode = OverwriteMode,
                    DeleteMode = DeleteMode,
                    VirtualCheckState = node.CheckState,
                };
                _contexts[node] = ctx;
            }
            return ctx;
        }

        public bool IsNodeFailed(DirectoryTreeNode node) => _failed.Contains(node);
        public void MarkFailed(DirectoryTreeNode node) => _failed.Add(node);
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    /// <summary>
    /// Cross-provider file move: source ≠ target provider triggers copy+delete fallback.
    /// File must be gone from source and present in target.
    /// </summary>
    [Fact]
    public async Task CrossProvider_File_UsesCopyDeleteFallback()
    {
        var (source, target) = MemoryFileSystemFixtures.CreatePair(
            s => s.WithFile("/src/file.txt", "content"u8));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(source, "/src");
        root.Files[0].CheckState = CheckState.Checked;

        var ctx = new MoveTestContext(root, source, target);
        var step = new MoveStep("/dest");

        var results = new List<TransformResult>();
        await foreach (var r in step.ApplyAsync(ctx, CancellationToken.None))
            results.Add(r);

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        Assert.Equal(SourceResult.Moved, results[0].SourceNodeResult);
        Assert.False(await source.ExistsAsync("/src/file.txt", CancellationToken.None));
        Assert.True(await target.ExistsAsync("/dest/file.txt", CancellationToken.None));
    }

    /// <summary>
    /// Cross-provider directory move: directories cannot be moved atomically across providers.
    /// The directory result must be IsSuccess=false; child files must be handled (skipped).
    /// </summary>
    [Fact]
    public async Task CrossProvider_Directory_MarkedFailed()
    {
        var (source, target) = MemoryFileSystemFixtures.CreatePair(
            s => s.WithDirectory("/src/subdir").WithFile("/src/subdir/file1.txt", "x"u8));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(source, "/src");
        root.Children[0].CheckState = CheckState.Checked; // selects subdir + file1.txt

        var ctx = new MoveTestContext(root, source, target);
        var step = new MoveStep("/dest");

        var results = new List<TransformResult>();
        await foreach (var r in step.ApplyAsync(ctx, CancellationToken.None))
            results.Add(r);

        // Only one result for the directory (children are skipped via handledNodes).
        Assert.Single(results);
        Assert.False(results[0].IsSuccess);
        Assert.Equal(SourceResult.None, results[0].SourceNodeResult);
        // Source must be untouched — the directory was not moved.
        Assert.True(await source.ExistsAsync("/src/subdir/file1.txt", CancellationToken.None));
    }

    /// <summary>
    /// Same-provider file move with CanAtomicMove=true uses MoveAsync (no copy+delete).
    /// File must be gone from source, present at destination.
    /// </summary>
    [Fact]
    public async Task SameProvider_AtomicMove_FileMoved()
    {
        var provider = MemoryFileSystemFixtures.Create(s => s.WithFile("/src/file.txt", "atomic"u8));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(provider, "/src");
        root.Files[0].CheckState = CheckState.Checked;

        // Same provider instance for both source and target → sameProvider = true.
        var ctx = new MoveTestContext(root, provider, provider);
        var step = new MoveStep("/dest");

        var results = new List<TransformResult>();
        await foreach (var r in step.ApplyAsync(ctx, CancellationToken.None))
            results.Add(r);

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        Assert.Equal(SourceResult.Moved, results[0].SourceNodeResult);
        Assert.False(await provider.ExistsAsync("/src/file.txt", CancellationToken.None));
        Assert.True(await provider.ExistsAsync("/dest/file.txt", CancellationToken.None));
    }

    /// <summary>
    /// Same-provider file move with CanAtomicMove=false falls back to copy+delete.
    /// Result must be Moved with content preserved at destination.
    /// </summary>
    [Fact]
    public async Task AtomicMoveDisabled_SameProvider_UsesCopyDeleteFallback()
    {
        var memory = MemoryFileSystemFixtures.Create(s => s.WithFile("/src/file.txt", "fallback"u8));
        var noAtomicMove = new CapabilityOverrideProvider(memory,
            new ProviderCapabilities(CanSeek: true, CanAtomicMove: false, MaxPathLength: int.MaxValue));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(memory, "/src");
        root.Files[0].CheckState = CheckState.Checked;

        // Same wrapped instance → sameProvider = true, canAtomicMove = false → fallback.
        var ctx = new MoveTestContext(root, noAtomicMove, noAtomicMove);
        var step = new MoveStep("/dest");

        var results = new List<TransformResult>();
        await foreach (var r in step.ApplyAsync(ctx, CancellationToken.None))
            results.Add(r);

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        Assert.Equal(SourceResult.Moved, results[0].SourceNodeResult);
        Assert.False(await memory.ExistsAsync("/src/file.txt", CancellationToken.None));
        Assert.True(await memory.ExistsAsync("/dest/file.txt", CancellationToken.None));
    }
}
