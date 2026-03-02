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
        public FileSystemProviderRegistry ProviderRegistry { get; }
        public OverwriteMode OverwriteMode => OverwriteMode.Always;
        public DeleteMode DeleteMode => DeleteMode.Permanent;

        public MoveTestContext(DirectoryTreeNode root, IFileSystemProvider source, IFileSystemProvider? target = null)
        {
            RootNode = root;
            SourceProvider = source;
            ProviderRegistry = new FileSystemProviderRegistry();
            // Either register target OR source but not both (because they have the same root)
            ProviderRegistry.Register(target ?? source);
        }

        public PipelineContext GetNodeContext(DirectoryTreeNode node)
        {
            if (!_contexts.TryGetValue(node, out var ctx))
            {
                ctx = new PipelineContext
                {
                    SourceNode = node,
                    SourceProvider = SourceProvider,
                    ProviderRegistry = ProviderRegistry,
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
        var sourceProvider = MemoryFileSystemFixtures.Create(s => s.WithFile("/src/file.txt", "content"u8));
        var targetProvider = MemoryFileSystemFixtures.Create(t => t.WithDirectory("dest"));

        var root = await MemoryFileSystemFixtures.BuildDirectoryTree(sourceProvider, "/src");
        root.Files[0].CheckState = CheckState.Checked;

        var ctx = new MoveTestContext(root, sourceProvider, targetProvider);
        var step = new MoveStep("/mem/dest");

        var results = new List<TransformResult>();
        await foreach (var r in step.ApplyAsync(ctx, CancellationToken.None))
            results.Add(r);

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        Assert.Equal(SourceResult.Moved, results[0].SourceNodeResult);
        Assert.False(await sourceProvider.ExistsAsync("/src/file.txt", CancellationToken.None));
        Assert.True(await targetProvider.ExistsAsync("/dest/file.txt", CancellationToken.None));
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
        var step = new MoveStep("/mem/dest");

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
        var step = new MoveStep("/mem/dest");

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
