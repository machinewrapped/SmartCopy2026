using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Strategy;
using SmartCopy.Core.Trash;

namespace SmartCopy.Tests.TestInfrastructure;

/// <summary>
/// Shared <see cref="IStepContext"/> fake for unit-testing pipeline steps. Mirrors the real
/// <c>PipelineRunner.StepContext</c>: caches a <see cref="PipelineContext"/> per node (so PathSegments
/// mutations persist across steps) and tracks failed nodes. Source (and optional target) providers are
/// registered in a fresh registry. Trash service and operational settings are injectable; everything
/// else uses sensible defaults. Replaces the near-identical context fakes previously hand-rolled in
/// each step-test file.
/// </summary>
internal sealed class FakeStepContext : IStepContext
{
    private readonly Dictionary<DirectoryTreeNode, PipelineContext> _contexts = new();
    private readonly HashSet<DirectoryTreeNode> _failed = new();

    public DirectoryNode RootNode { get; }
    public IFileSystemProvider SourceProvider { get; }
    public FileSystemProviderRegistry ProviderRegistry { get; }
    public bool ShowHiddenFiles { get; }
    public bool AllowDeleteReadOnly { get; }
    public ITrashService TrashService { get; }
    public OperationalSettings OperationalSettings { get; }
    public ICopyStrategyPolicy CopyStrategyPolicy { get; }

    public FakeStepContext(
        DirectoryNode root,
        IFileSystemProvider source,
        IFileSystemProvider? target = null,
        ITrashService? trashService = null,
        OperationalSettings? settings = null,
        ICopyStrategyPolicy? copyStrategyPolicy = null)
    {
        RootNode = root;
        SourceProvider = source;
        TrashService = trashService ?? new NullTrashService();
        OperationalSettings = settings ?? new OperationalSettings();
        CopyStrategyPolicy = copyStrategyPolicy ?? DefaultCopyStrategyPolicy.Instance;

        ProviderRegistry = new FileSystemProviderRegistry();
        ProviderRegistry.Register(source);
        if (target != null && target != source)
            ProviderRegistry.Register(target);
    }

    public PipelineContext GetNodeContext(DirectoryTreeNode node)
    {
        if (!_contexts.TryGetValue(node, out var context))
        {
            context = new PipelineContext
            {
                SourceNode = node,
                SourceProvider = SourceProvider,
                ProviderRegistry = ProviderRegistry,
                PathSegments = node.RelativePathSegments.Length > 0 ? node.RelativePathSegments : [node.Name],
                CurrentExtension = Path.GetExtension(node.Name).TrimStart('.'),
                VirtualCheckState = node.CheckState,
            };
            _contexts[node] = context;
        }
        return context;
    }

    public bool IsNodeFailed(DirectoryTreeNode node) => _failed.Contains(node);
    public void MarkFailed(DirectoryTreeNode node) => _failed.Add(node);
}
