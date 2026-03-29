using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Pipeline.Validation;
using SmartCopy.Core.Selection;
using SmartCopy.Core.Trash;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

public sealed class SelectionFileStepTests : IDisposable
{
    // -------------------------------------------------------------------------
    // Test infrastructure
    // -------------------------------------------------------------------------

    private readonly List<string> _tempFiles = [];

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    private string GetTempFile(string extension = ".txt")
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), extension);
        _tempFiles.Add(path);
        return path;
    }

    private sealed class TestStepContext : IStepContext
    {
        private readonly Dictionary<DirectoryTreeNode, PipelineContext> _contexts = new();
        private readonly HashSet<DirectoryTreeNode> _failed = new();

        public DirectoryNode RootNode { get; }
        public IFileSystemProvider SourceProvider { get; }
        public FileSystemProviderRegistry ProviderRegistry { get; }
        public bool ShowHiddenFiles => false;
        public bool AllowDeleteReadOnly => false;
        public ITrashService TrashService { get; } = new NullTrashService();

        public TestStepContext(DirectoryNode root, IFileSystemProvider provider)
        {
            RootNode = root;
            SourceProvider = provider;
            ProviderRegistry = new FileSystemProviderRegistry();
            ProviderRegistry.Register(provider);
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
                    PathSegments = node.RelativePathSegments.Length > 0
                        ? node.RelativePathSegments
                        : [node.Name],
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

    /// <summary>Tree with /src/file_a.txt (checked) and /src/file_b.txt (unchecked).</summary>
    private static async Task<(DirectoryNode Root, FileNode FileA, FileNode FileB, IFileSystemProvider Provider)>
        MakeTwoFileTree()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithFile("/src/file_a.txt", "a"u8)
            .WithFile("/src/file_b.txt", "b"u8));
        var root = await provider.BuildDirectoryTree("/src");
        var fileA = root.Files.Single(f => f.Name == "file_a.txt");
        var fileB = root.Files.Single(f => f.Name == "file_b.txt");
        fileA.CheckState = CheckState.Checked;
        fileB.CheckState = CheckState.Unchecked;
        return (root, fileA, fileB, provider);
    }

    private static async Task<string> WriteTxtSelectionFile(IEnumerable<string> paths)
    {
        var path = Path.GetTempFileName();
        await File.WriteAllLinesAsync(path, paths);
        return path;
    }

    // -------------------------------------------------------------------------
    // SelectionManager.AddFromSnapshot
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddFromSnapshot_ChecksMatchedNodes_LeavesOthersUntouched()
    {
        var (root, fileA, fileB, _) = await MakeTwoFileTree();
        // fileA already checked; fileB unchecked; snapshot references only fileB
        var snapshot = new SelectionSnapshot(["file_b.txt"]);
        new SelectionManager().AddFromSnapshot(root, snapshot);

        Assert.Equal(CheckState.Checked, fileA.CheckState); // unchanged
        Assert.Equal(CheckState.Checked, fileB.CheckState); // now checked
    }

    [Fact]
    public async Task AddFromSnapshot_ReturnsUnmatchedPaths()
    {
        var (root, _, _, _) = await MakeTwoFileTree();
        var snapshot = new SelectionSnapshot(["file_a.txt", "nonexistent.txt"]);
        var result = new SelectionManager().AddFromSnapshot(root, snapshot);

        Assert.Equal(1, result.MatchedCount);
        Assert.Single(result.UnmatchedPaths);
        Assert.Equal("nonexistent.txt", result.UnmatchedPaths[0]);
    }

    // -------------------------------------------------------------------------
    // SaveSelectionToFileStep
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SaveSelectionToFileStep_ApplyAsync_SavesSelectedRelativePaths()
    {
        var (root, fileA, _, provider) = await MakeTwoFileTree();
        var outPath = GetTempFile(".txt");
        var step = new SaveSelectionToFileStep(outPath, useAbsolutePaths: false);
        var context = new TestStepContext(root, provider);

        await foreach (var _ in step.ApplyAsync(context, CancellationToken.None)) { }

        var saved = await new SelectionSerializer().LoadAsync(outPath);
        Assert.Contains("file_a.txt", saved.Paths);
        Assert.DoesNotContain("file_b.txt", saved.Paths);
    }

    [Fact]
    public async Task SaveSelectionToFileStep_ApplyAsync_SavesAbsolutePaths_WhenFlagSet()
    {
        var (root, fileA, _, provider) = await MakeTwoFileTree();
        var outPath = GetTempFile(".txt");
        var step = new SaveSelectionToFileStep(outPath, useAbsolutePaths: true);
        var context = new TestStepContext(root, provider);

        await foreach (var _ in step.ApplyAsync(context, CancellationToken.None)) { }

        var saved = await new SelectionSerializer().LoadAsync(outPath);
        Assert.Contains(saved.Paths, p => p.Contains("file_a"));
        // All saved paths should be absolute (contain root segment)
        Assert.All(saved.Paths, p => Assert.True(p.Length > "file_a.txt".Length));
    }

    [Fact]
    public async Task SaveSelectionToFileStep_PreviewAsync_DoesNotWriteFile()
    {
        var (root, _, _, provider) = await MakeTwoFileTree();
        var outPath = GetTempFile(".txt");
        var step = new SaveSelectionToFileStep(outPath, useAbsolutePaths: false);
        var context = new TestStepContext(root, provider);

        await foreach (var _ in step.PreviewAsync(context, CancellationToken.None)) { }

        Assert.False(File.Exists(outPath) && new FileInfo(outPath).Length > 0,
            "Preview should not write to file");
    }

    [Fact]
    public async Task SaveSelectionToFileStep_Validate_BlocksOnEmptyPath()
    {
        var step = new SaveSelectionToFileStep(string.Empty);
        var context = new StepValidationContext(selectedFileCount: 1, numFilterIncludedFiles: 3);

        await step.Validate(context);

        Assert.Single(context.Issues);
        Assert.Equal(PipelineValidationSeverity.Blocking, context.Issues[0].Severity);
    }

    [Fact]
    public async Task SaveSelectionToFileStep_Validate_NoIssues_WhenPathSet()
    {
        var step = new SaveSelectionToFileStep("/tmp/out.txt");
        var context = new StepValidationContext(selectedFileCount: 2, selectedBytes: 100,
            numFilterIncludedFiles: 3, totalFilterIncludedBytes: 200);

        await step.Validate(context);

        Assert.Empty(context.Issues);
        // Post-condition: selection unchanged
        Assert.Equal(2, context.SelectedFileCount);
        Assert.Equal(100, context.SelectedBytes);
    }

    [Fact]
    public void SaveSelectionToFileStep_Config_RoundTrips()
    {
        var step = new SaveSelectionToFileStep("/my/path.sc2sel", useAbsolutePaths: true);
        var restored = PipelineStepFactory.FromConfig(step.Config) as SaveSelectionToFileStep;

        Assert.NotNull(restored);
        Assert.Equal("/my/path.sc2sel", restored.FilePath);
        Assert.True(restored.UseAbsolutePaths);
    }

    // -------------------------------------------------------------------------
    // AddSelectionFromFileStep
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddSelectionFromFileStep_ApplyAsync_AddsMatchedNodes_LeavesOthersUntouched()
    {
        var (root, fileA, fileB, provider) = await MakeTwoFileTree();
        // fileA=Checked, fileB=Unchecked; selection file contains only file_b.txt
        var selFile = await WriteTxtSelectionFile(["file_b.txt"]);
        _tempFiles.Add(selFile);
        var step = new AddSelectionFromFileStep(selFile);
        var context = new TestStepContext(root, provider);

        await foreach (var _ in step.ApplyAsync(context, CancellationToken.None)) { }

        Assert.Equal(CheckState.Checked, fileA.CheckState); // unchanged
        Assert.Equal(CheckState.Checked, fileB.CheckState); // added
    }

    [Fact]
    public async Task AddSelectionFromFileStep_PreviewAsync_SetsVirtualCheckState_DoesNotMutateReal()
    {
        var (root, fileA, fileB, provider) = await MakeTwoFileTree();
        var selFile = await WriteTxtSelectionFile(["file_b.txt"]);
        _tempFiles.Add(selFile);
        var step = new AddSelectionFromFileStep(selFile);
        var context = new TestStepContext(root, provider);

        var results = new List<TransformResult>();
        await foreach (var r in step.PreviewAsync(context, CancellationToken.None))
            results.Add(r);

        Assert.Single(results);
        Assert.NotNull(results[0].ActionSummary);
        Assert.Equal(CheckState.Checked, context.GetNodeContext(fileB).VirtualCheckState);
        Assert.Equal(CheckState.Unchecked, fileB.CheckState); // real state unchanged
    }

    [Fact]
    public async Task AddSelectionFromFileStep_Validate_BlocksOnEmptyPath()
    {
        var step = new AddSelectionFromFileStep(string.Empty);
        var context = new StepValidationContext(selectedFileCount: 1, numFilterIncludedFiles: 3);

        await step.Validate(context);

        Assert.Single(context.Issues);
        Assert.Equal(PipelineValidationSeverity.Blocking, context.Issues[0].Severity);
    }

    [Fact]
    public async Task AddSelectionFromFileStep_Validate_BlocksWhenFileNotFound()
    {
        var step = new AddSelectionFromFileStep("/nonexistent/path.txt");
        var context = new StepValidationContext(selectedFileCount: 1, numFilterIncludedFiles: 3);

        await step.Validate(context);

        Assert.Single(context.Issues);
        Assert.Equal("Step.FileNotFound", context.Issues[0].Code);
    }

    [Fact]
    public async Task AddSelectionFromFileStep_Validate_SetsMaxPostCondition_WhenFileExists()
    {
        var selFile = await WriteTxtSelectionFile(["file_a.txt"]);
        _tempFiles.Add(selFile);
        var step = new AddSelectionFromFileStep(selFile);
        var context = new StepValidationContext(
            selectedFileCount: 1, selectedBytes: 50,
            numFilterIncludedFiles: 5, totalFilterIncludedBytes: 500,
            providerRegistry: new FileSystemProviderRegistry());

        await step.Validate(context);

        Assert.Empty(context.Issues);
        Assert.Equal(5, context.SelectedFileCount);
        Assert.Equal(500, context.SelectedBytes);
    }

    [Fact]
    public void AddSelectionFromFileStep_Config_RoundTrips()
    {
        var step = new AddSelectionFromFileStep("/my/selection.sc2sel");
        var restored = PipelineStepFactory.FromConfig(step.Config) as AddSelectionFromFileStep;

        Assert.NotNull(restored);
        Assert.Equal("/my/selection.sc2sel", restored.FilePath);
    }

    // -------------------------------------------------------------------------
    // RemoveSelectionFromFileStep
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RemoveSelectionFromFileStep_ApplyAsync_DeselectsMatchedNodes_LeavesOthers()
    {
        var (root, fileA, fileB, provider) = await MakeTwoFileTree();
        // fileA=Checked, fileB=Unchecked; selection file contains file_a.txt
        var selFile = await WriteTxtSelectionFile(["file_a.txt"]);
        _tempFiles.Add(selFile);
        var step = new RemoveSelectionFromFileStep(selFile);
        var context = new TestStepContext(root, provider);

        await foreach (var _ in step.ApplyAsync(context, CancellationToken.None)) { }

        Assert.Equal(CheckState.Unchecked, fileA.CheckState); // deselected
        Assert.Equal(CheckState.Unchecked, fileB.CheckState); // unchanged
    }

    [Fact]
    public async Task RemoveSelectionFromFileStep_PreviewAsync_SetsVirtualCheckState_DoesNotMutateReal()
    {
        var (root, fileA, fileB, provider) = await MakeTwoFileTree();
        var selFile = await WriteTxtSelectionFile(["file_a.txt"]);
        _tempFiles.Add(selFile);
        var step = new RemoveSelectionFromFileStep(selFile);
        var context = new TestStepContext(root, provider);

        var results = new List<TransformResult>();
        await foreach (var r in step.PreviewAsync(context, CancellationToken.None))
            results.Add(r);

        Assert.Single(results);
        Assert.NotNull(results[0].ActionSummary);
        Assert.Equal(CheckState.Unchecked, context.GetNodeContext(fileA).VirtualCheckState);
        Assert.Equal(CheckState.Checked, fileA.CheckState); // real state unchanged
    }

    [Fact]
    public async Task RemoveSelectionFromFileStep_Validate_BlocksOnEmptyPath()
    {
        var step = new RemoveSelectionFromFileStep(string.Empty);
        var context = new StepValidationContext(selectedFileCount: 2, numFilterIncludedFiles: 3);

        await step.Validate(context);

        Assert.Single(context.Issues);
        Assert.Equal(PipelineValidationSeverity.Blocking, context.Issues[0].Severity);
    }

    [Fact]
    public async Task RemoveSelectionFromFileStep_Validate_BlocksWhenFileNotFound()
    {
        var step = new RemoveSelectionFromFileStep("/nonexistent/path.txt");
        var context = new StepValidationContext(selectedFileCount: 2, numFilterIncludedFiles: 3);

        await step.Validate(context);

        Assert.Single(context.Issues);
        Assert.Equal("Step.FileNotFound", context.Issues[0].Code);
    }

    [Fact]
    public async Task RemoveSelectionFromFileStep_Validate_SetsZeroPostCondition_WhenFileExists()
    {
        var selFile = await WriteTxtSelectionFile(["file_a.txt"]);
        _tempFiles.Add(selFile);
        var step = new RemoveSelectionFromFileStep(selFile);
        var context = new StepValidationContext(
            selectedFileCount: 3, selectedBytes: 300,
            numFilterIncludedFiles: 5, totalFilterIncludedBytes: 500,
            providerRegistry: new FileSystemProviderRegistry());

        await step.Validate(context);

        Assert.Empty(context.Issues);
        Assert.Equal(0, context.SelectedFileCount);
        Assert.Equal(0, context.SelectedBytes);
    }

    [Fact]
    public void RemoveSelectionFromFileStep_Config_RoundTrips()
    {
        var step = new RemoveSelectionFromFileStep("/my/exclude.txt");
        var restored = PipelineStepFactory.FromConfig(step.Config) as RemoveSelectionFromFileStep;

        Assert.NotNull(restored);
        Assert.Equal("/my/exclude.txt", restored.FilePath);
    }
}
