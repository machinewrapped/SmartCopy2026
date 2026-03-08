using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

public sealed class FreeSpaceCheckTests
{
    // 100 bytes of content shared across test files
    private static readonly byte[] FileContent = new byte[100];

    private static (MemoryFileSystemProvider provider, DirectoryTreeNode root, FileSystemProviderRegistry registry) MakeSource(
        string volumeId = "SRC")
    {
        var provider = MemoryFileSystemFixtures.Create(b => b
            .WithFile("/src/file.txt", FileContent)
            .WithDirectory("/other"), // unselected sibling keeps root Indeterminate
            volumeId: volumeId);

        var root = provider.BuildDirectoryTree().GetAwaiter().GetResult();

        // Check only /src — root becomes Indeterminate, so MoveStep won't collapse /src.
        root.FindNodeByPathSegments(["src"])!.CheckState = CheckState.Checked;

        return (provider, root, provider.CreateRegistry());
    }

    private static MemoryFileSystemProvider MakeTarget(long? capacity, string customRootPath, string volumeId = "DST")
    {
        var target = MemoryFileSystemFixtures.Create(b => b
            .WithDirectory("/dst"),
            customRootPath: customRootPath,
            volumeId: volumeId);

        target.SimulatedCapacity = capacity;
        return target;
    }

    [Fact]
    public async Task Copy_SufficientSpace_NoError()
    {
        var (source, root, registry) = MakeSource();
        var target = MakeTarget(capacity: 1_000_000, customRootPath: "/target");
        registry.Register(target);

        var runner = new PipelineRunner(new TransformPipeline([new CopyStep("/target/dst")]));
        var plan = await runner.PreviewAsync(new PipelineJob
        {
            RootNode = root,
            SourceProvider = source,
            ProviderRegistry = registry,
        });

        Assert.Empty(plan.Errors);
    }

    [Fact]
    public async Task Copy_InsufficientSpace_Warning()
    {
        var (source, root, registry) = MakeSource();
        var target = MakeTarget(capacity: 10, customRootPath: "/target"); // only 10 bytes free
        registry.Register(target);

        var runner = new PipelineRunner(new TransformPipeline([new CopyStep("/target/dst")]));
        var plan = await runner.PreviewAsync(new PipelineJob
        {
            RootNode = root,
            SourceProvider = source,
            ProviderRegistry = registry,
        });

        Assert.Single(plan.Warnings);
        Assert.Contains("Not enough space", plan.Warnings[0]);
    }

    [Fact]
    public async Task TwoCopySteps_SameVolume_CumulativeConsumptionChecked()
    {
        // 600 bytes free, each Copy step needs 100 bytes → both fit individually,
        // but if there were 150 bytes per step and only 200 free the second would fail.
        // Use 400 bytes per step with 600 free so second step (400 > 200 remaining) warns.
        var provider = MemoryFileSystemFixtures.Create(b => b
            .WithFile("/src/file.txt", new byte[400])
            .WithDirectory("/other"),
            volumeId: "SRC");
        var root = await provider.BuildDirectoryTree();
        root.FindNodeByPathSegments(["src"])!.CheckState = CheckState.Checked;

        var registry = provider.CreateRegistry();

        var target = MakeTarget(capacity: 600, customRootPath: "/target");
        registry.Register(target);

        var runner = new PipelineRunner(new TransformPipeline([
            new CopyStep("/target/dst1"),
            new CopyStep("/target/dst2"),
        ]));
        var plan = await runner.PreviewAsync(new PipelineJob
        {
            RootNode = root,
            SourceProvider = provider,
            ProviderRegistry = registry,
        });

        // First step: 400 <= 600 free → no warning; cache updated to 200.
        // Second step: 400 > 200 → warning.
        Assert.Single(plan.Warnings);
        Assert.Contains("Not enough space", plan.Warnings[0]);
    }

    [Fact]
    public async Task Copy_NoCapability_NoError()
    {
        var (source, root, registry) = MakeSource();
        // Target has no capacity set → CanQueryFreeSpace = false
        var target = MakeTarget(capacity: null, customRootPath: "/target");
        registry.Register(target);

        var runner = new PipelineRunner(new TransformPipeline([new CopyStep("/target/dst")]));
        var plan = await runner.PreviewAsync(new PipelineJob
        {
            RootNode = root,
            SourceProvider = source,
            ProviderRegistry = registry,
        });

        Assert.Empty(plan.Errors);
    }

    [Fact]
    public async Task Move_SameVolume_NoSpaceCheck()
    {
        // Same provider, same VolumeId → atomic move, no space check
        var provider = MemoryFileSystemFixtures.Create(b => b
            .WithFile("/src/file.txt", FileContent)
            .WithDirectory("/other") // keeps root Indeterminate
            .WithDirectory("/dst"),
            volumeId: "VOL");
        provider.SimulatedCapacity = FileContent.Length + 1; // only 1 byte free, but should not trigger error
        var root = await provider.BuildDirectoryTree();
        root.FindNodeByPathSegments(["src"])!.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("/mem/dst")]));
        var plan = await runner.PreviewAsync(new PipelineJob
        {
            RootNode = root,
            SourceProvider = provider,
            ProviderRegistry = provider.CreateRegistry(),
        });

        Assert.Empty(plan.Errors);
    }

    [Fact]
    public async Task Move_CrossVolume_SufficientSpace_NoError()
    {
        var (source, root, registry) = MakeSource(volumeId: "SRC");
        var target = MakeTarget(capacity: 1_000_000, customRootPath: "/target", volumeId: "DST");
        registry.Register(target);

        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("/target/dst")]));
        var plan = await runner.PreviewAsync(new PipelineJob
        {
            RootNode = root,
            SourceProvider = source,
            ProviderRegistry = registry,
        });

        Assert.Empty(plan.Errors);
    }

    [Fact]
    public async Task Move_CrossVolume_InsufficientSpace_Warning()
    {
        var (source, root, registry) = MakeSource(volumeId: "SRC");
        var target = MakeTarget(capacity: 10, customRootPath: "/target", volumeId: "DST"); // only 10 bytes
        registry.Register(target);

        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("/target/dst")]));
        var plan = await runner.PreviewAsync(new PipelineJob
        {
            RootNode = root,
            SourceProvider = source,
            ProviderRegistry = registry,
        });

        Assert.NotEmpty(plan.Warnings);
        Assert.Contains("Not enough space", plan.Warnings[0]);
    }
}
