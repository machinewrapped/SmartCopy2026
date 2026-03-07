using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Trash;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

public sealed class DeleteStepTrashTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Test infrastructure
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class SpyTrashService : ITrashService
    {
        public bool IsAvailable { get; set; } = true;
        public List<string> TrashedPaths { get; } = [];

        public Task TrashAsync(string fullPath, CancellationToken ct)
        {
            TrashedPaths.Add(fullPath);
            return Task.CompletedTask;
        }
    }

    private sealed class TestStepContext : IStepContext
    {
        private readonly Dictionary<DirectoryTreeNode, PipelineContext> _contexts = new();
        private readonly HashSet<DirectoryTreeNode> _failed = new();

        public DirectoryTreeNode RootNode { get; }
        public IFileSystemProvider SourceProvider { get; }
        public FileSystemProviderRegistry ProviderRegistry { get; }
        public bool ShowHiddenFiles { get; }
        public bool AllowDeleteReadOnly { get; }
        public ITrashService TrashService { get; }

        public TestStepContext(
            DirectoryTreeNode root,
            IFileSystemProvider provider,
            ITrashService trashService)
        {
            RootNode = root;
            SourceProvider = provider;
            ProviderRegistry = new FileSystemProviderRegistry();
            ProviderRegistry.Register(provider);
            TrashService = trashService;
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
                    VirtualCheckState = node.CheckState,
                };
                _contexts[node] = ctx;
            }
            return ctx;
        }

        public bool IsNodeFailed(DirectoryTreeNode node) => _failed.Contains(node);
        public void MarkFailed(DirectoryTreeNode node) => _failed.Add(node);
    }

    private static CapabilityOverrideProvider WithCanTrash(MemoryFileSystemProvider inner, bool canTrash)
        => new(inner, new ProviderCapabilities(
            CanSeek: true,
            CanAtomicMove: true,
            CanWatch: false,
            MaxPathLength: int.MaxValue,
            CanTrash: canTrash));

    private static async Task<(DirectoryTreeNode Root, DirectoryTreeNode File, MemoryFileSystemProvider Provider)>
        MakeTree()
    {
        // Two files so that selecting one does not make root.IsSelected=true.
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithFile("/src/file.txt", "data"u8)
            .WithFile("/src/other.txt", "keep"u8));
        var root = await provider.BuildDirectoryTree("/src");
        var file = root.Files.Single(f => f.Name == "file.txt");
        file.CheckState = CheckState.Checked;
        return (root, file, provider);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test cases
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Trash mode + CanTrash=true + IsAvailable=true → TrashAsync called, result is Trashed.
    /// </summary>
    [Fact]
    public async Task TrashMode_CanTrash_Available_CallsTrashService()
    {
        var (root, file, memProvider) = await MakeTree();
        var provider = WithCanTrash(memProvider, canTrash: true);
        var trashSpy = new SpyTrashService { IsAvailable = true };
        var context = new TestStepContext(root, provider, trashSpy);

        var step = new DeleteStep(DeleteMode.Trash);
        var results = await step.ApplyAsync(context, CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        Assert.Equal(SourceResult.Trashed, results[0].SourceNodeResult);
        Assert.Contains(file.FullPath, trashSpy.TrashedPaths);
    }

    /// <summary>
    /// Trash mode + CanTrash=false → falls back to DeleteAsync, result is Deleted.
    /// </summary>
    [Fact]
    public async Task TrashMode_CanTrashFalse_FallsBackToDelete()
    {
        var (root, file, memProvider) = await MakeTree();
        var provider = WithCanTrash(memProvider, canTrash: false);
        var trashSpy = new SpyTrashService { IsAvailable = true };
        var context = new TestStepContext(root, provider, trashSpy);

        var step = new DeleteStep(DeleteMode.Trash);
        var results = await step.ApplyAsync(context, CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        Assert.Equal(SourceResult.Deleted, results[0].SourceNodeResult);
        Assert.Empty(trashSpy.TrashedPaths);
    }

    /// <summary>
    /// Trash mode + CanTrash=true + IsAvailable=false → falls back to DeleteAsync, result is Deleted.
    /// </summary>
    [Fact]
    public async Task TrashMode_ServiceUnavailable_FallsBackToDelete()
    {
        var (root, file, memProvider) = await MakeTree();
        var provider = WithCanTrash(memProvider, canTrash: true);
        var trashSpy = new SpyTrashService { IsAvailable = false };
        var context = new TestStepContext(root, provider, trashSpy);

        var step = new DeleteStep(DeleteMode.Trash);
        var results = await step.ApplyAsync(context, CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        Assert.Equal(SourceResult.Deleted, results[0].SourceNodeResult);
        Assert.Empty(trashSpy.TrashedPaths);
    }

    /// <summary>
    /// Permanent mode → DeleteAsync called, TrashService NOT called at all.
    /// </summary>
    [Fact]
    public async Task PermanentMode_NeverCallsTrashService()
    {
        var (root, file, memProvider) = await MakeTree();
        var provider = WithCanTrash(memProvider, canTrash: true);
        var trashSpy = new SpyTrashService { IsAvailable = true };
        var context = new TestStepContext(root, provider, trashSpy);

        var step = new DeleteStep(DeleteMode.Permanent);
        var results = await step.ApplyAsync(context, CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        Assert.Equal(SourceResult.Deleted, results[0].SourceNodeResult);
        Assert.Empty(trashSpy.TrashedPaths);
    }

    /// <summary>
    /// Preview: Trash mode + CanTrash=true → PlannedAction has SourceResult.Trashed.
    /// </summary>
    [Fact]
    public async Task Preview_TrashMode_CanTrash_YieldsTrashed()
    {
        var (root, file, memProvider) = await MakeTree();
        var provider = WithCanTrash(memProvider, canTrash: true);
        var context = new TestStepContext(root, provider, new NullTrashService());

        var step = new DeleteStep(DeleteMode.Trash);
        var results = await step.PreviewAsync(context, CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.Equal(SourceResult.Trashed, results[0].SourceNodeResult);
    }

    /// <summary>
    /// Preview: Trash mode + CanTrash=false → PlannedAction has SourceResult.Deleted.
    /// </summary>
    [Fact]
    public async Task Preview_TrashMode_CanTrashFalse_YieldsDeleted()
    {
        var (root, file, memProvider) = await MakeTree();
        var provider = WithCanTrash(memProvider, canTrash: false);
        var context = new TestStepContext(root, provider, new NullTrashService());

        var step = new DeleteStep(DeleteMode.Trash);
        var results = await step.PreviewAsync(context, CancellationToken.None).ToListAsync();

        Assert.Single(results);
        Assert.Equal(SourceResult.Deleted, results[0].SourceNodeResult);
    }
}

internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }
}
