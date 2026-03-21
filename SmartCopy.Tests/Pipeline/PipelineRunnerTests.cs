using System.Text;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.FileSystem;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

public sealed class PipelineRunnerTests
{
    [Fact]
    public async Task CopyPipeline_PreviewsAndExecutes()
    {
        var provider = MemoryFileSystemFixtures.Create(source => source
                .WithDirectory("/source")
                .WithFile("/source/song.flac", Encoding.UTF8.GetBytes("audio"))
                .WithDirectory("/Mirror"));

        var sourceRoot = await provider.BuildDirectoryTree();
        var sourceNode = sourceRoot.FindNodeByPathSegments(["source", "song.flac"]);
        Assert.NotNull(sourceNode);

        sourceNode.CheckState = CheckState.Checked;
        var pipeline = new TransformPipeline([new CopyStep("mem://Mirror")]);
        var runner = new PipelineRunner(pipeline);

        var job = new PipelineJob
        {
            RootNode       = sourceRoot,
            SourceProvider = provider,
            ProviderRegistry = provider.CreateRegistry(),
        };

        var plan = await runner.PreviewAsync(job, CancellationToken.None);

        Assert.Single(plan.Actions);
        Assert.Contains("/Mirror", plan.Actions[0].DestinationPath);

        var results = await runner.ExecuteAsync(job);

        Assert.Contains(results, r => r.SourceNodeResult == SourceResult.Copied && r.IsSuccess);
        Assert.True(await provider.ExistsAsync("/Mirror/source/song.flac", CancellationToken.None));
    }

    [Fact]
    public async Task DeletePipeline_RequiresPreviewBeforeExecute()
    {
        var provider = MemoryFileSystemFixtures.Create(fixture => fixture
            .WithDirectory("/source")
            .WithFile("/source/delete-me.txt", "x"u8));

        var root = await provider.BuildDirectoryTree();
        var node = root.FindNodeByPathSegments(["source", "delete-me.txt"]);
        Assert.NotNull(node);
        node.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new DeleteStep()]));
        var registry = provider.CreateRegistry();

        var job = new PipelineJob
        {
            RootNode       = root,
            SourceProvider = provider,
            ProviderRegistry = registry,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.ExecuteAsync(job));
    }

    [Fact]
    public async Task DeletePipeline_RemovesSourceFile_AfterPreview()
    {
        var provider = MemoryFileSystemFixtures.Create(fixture => fixture
            .WithDirectory("/source")
            .WithFile("/source/delete-me.txt", "x"u8));

        var root = await provider.BuildDirectoryTree();
        var node = root.FindNodeByPathSegments(["source", "delete-me.txt"]);
        Assert.NotNull(node);
        node.CheckState = CheckState.Checked;
        var runner = new PipelineRunner(new TransformPipeline([new DeleteStep()]));
        var registry = provider.CreateRegistry();

        var job = new PipelineJob
        {
            RootNode       = root,
            SourceProvider = provider,
            ProviderRegistry = registry,
        };

        await runner.PreviewAsync(job, ct: CancellationToken.None);
        await runner.ExecuteAsync(job);

        Assert.False(await provider.ExistsAsync("/source/delete-me.txt", CancellationToken.None));
    }

    [Fact]
    public async Task DeletePipeline_PreviewReportsAllSelectedDescendants()
    {
        var provider = MemoryFileSystemFixtures.Create(fixture => fixture
            .WithDirectory("/source")
            .WithFile("/source/f1.txt", "x"u8)
            .WithFile("/source/f2.txt", "y"u8)
            .WithDirectory("/source/sub")
            .WithFile("/source/sub/f3.txt", "z"u8));

        DirectoryNode root = await provider.BuildDirectoryTree();

        var sourceNode = root.FindNodeByPathSegments(["source"]) as DirectoryNode;
        Assert.NotNull(sourceNode);
        sourceNode.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new DeleteStep()]));
        var registry = provider.CreateRegistry();

        var job = new PipelineJob
        {
            RootNode       = root,
            SourceProvider = provider,
            ProviderRegistry = registry,
        };

        var plan = await runner.PreviewAsync(job, CancellationToken.None);

        Assert.Equal(6, plan.Actions.Count);
        Assert.Contains(plan.Actions, a => a.SourcePath == "");
        Assert.Contains(plan.Actions, a => a.SourcePath == "source");
        Assert.Contains(plan.Actions, a => a.SourcePath == "source/f1.txt");
        Assert.Contains(plan.Actions, a => a.SourcePath == "source/f2.txt");
        Assert.Contains(plan.Actions, a => a.SourcePath == "source/sub");
        Assert.Contains(plan.Actions, a => a.SourcePath == "source/sub/f3.txt");
    }

    [Fact]
    public async Task FlattenThenCopy_FlattensPathAtDestination()
    {
        var provider = MemoryFileSystemFixtures.Create(source => source
                .WithDirectory("/source")
                .WithDirectory("/source/deep")
                .WithDirectory("/source/deep/folder")
                .WithFile("/source/deep/folder/track.mp3", "x"u8)
                .WithDirectory("/out"));

        var sourceRoot = await provider.BuildDirectoryTree();
        var node = sourceRoot.FindNodeByPathSegments(["source", "deep", "folder", "track.mp3"]);
        Assert.NotNull(node);
        node.CheckState = CheckState.Checked;
        var runner = new PipelineRunner(new TransformPipeline(
        [
            new FlattenStep(),
            new CopyStep("mem://out"),
        ]));

        await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = sourceRoot,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
            });

        Assert.True(await provider.ExistsAsync("/out/track.mp3", CancellationToken.None));
    }

    [Fact]
    public async Task FlattenThenCopy_PreviewReportsFlattened_DestinationPath()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
                .WithDirectory("/source/deep/folder")
                .WithFile("/source/deep/folder/track.mp3", "x"u8)
                .WithDirectory("/out"));

        var sourceRoot = await provider.BuildDirectoryTree();
        var node = sourceRoot.FindNodeByPathSegments(["source", "deep", "folder", "track.mp3"]);
        Assert.NotNull(node);
        node.CheckState = CheckState.Checked;
        var runner = new PipelineRunner(new TransformPipeline(
        [
            new FlattenStep(),
            new CopyStep("mem://out"),
        ]));

        var plan = await runner.PreviewAsync(
            new PipelineJob
            {
                RootNode       = sourceRoot,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
            },
            CancellationToken.None);

        var copyAction = plan.Actions.Single(a => a.SourceResult == SourceResult.Copied);
        Assert.Equal("mem://out/track.mp3", copyAction.DestinationPath);
    }

    [Fact]
    public async Task MoveStep_SkipsExistingDestination_WhenOverwriteModeIsSkip()
    {
        var provider = MemoryFileSystemFixtures.Create(fixture => fixture
            .WithDirectory("/source")
            .WithDirectory("/dest")
            .WithFile("/source/song.mp3", "original"u8)
            .WithFile("/dest/source/song.mp3", "existing"u8));

        var sourceRoot = await provider.BuildDirectoryTree();
        var node = sourceRoot.FindNodeByPathSegments(["source", "song.mp3"]);
        Assert.NotNull(node);
        node.CheckState = CheckState.Checked;
        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("mem://dest")]));

        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = sourceRoot,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
            });

        Assert.Contains(results, r => r.SourceNodeResult == SourceResult.Skipped);
        // Source must not have been deleted.
        Assert.True(await provider.ExistsAsync("/source/song.mp3", CancellationToken.None));
        // Destination must remain unchanged.
        await using var stream = await provider.OpenReadAsync("/dest/source/song.mp3", CancellationToken.None);
        var bytes = new byte[stream.Length];
        _ = await stream.ReadAsync(bytes, CancellationToken.None);
        Assert.Equal("existing"u8.ToArray(), bytes);
    }

    [Fact]
    public async Task ExecutablePipeline_RequiresAtLeastOneSelectedInput()
    {
        var provider = MemoryFileSystemFixtures.Create(fixture => fixture
            .WithDirectory("/source")
            .WithDirectory("/dest"));
        var root = await provider.BuildDirectoryTree();
        var runner = new PipelineRunner(new TransformPipeline([new CopyStep("mem://dest")]));

        var emptyJob = new PipelineJob
        {
            RootNode       = root,
            SourceProvider = provider,
            ProviderRegistry = provider.CreateRegistry(),
        };

        var previewError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.PreviewAsync(emptyJob, CancellationToken.None));

        Assert.Contains("At least one file must be selected", previewError.Message);

        var executeError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.ExecuteAsync(emptyJob));

        Assert.Contains("At least one file must be selected", executeError.Message);
    }

    [Fact]
    public async Task CrossProviderPipeline_RoutesToCorrectProviders_Execute()
    {
        var sourceProvider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/source")
            .WithFile("/source/a.txt", "a"u8)
            .WithFile("/source/b.txt", "b"u8));

        var targetProviderA = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/destA"), customRootPath: "mem://targetA");

        var targetProviderB = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/destB"), customRootPath: "mem://targetB");

        var root = await sourceProvider.BuildDirectoryTree();
        var nodeA = root.FindNodeByPathSegments(["source", "a.txt"]);
        var nodeB = root.FindNodeByPathSegments(["source", "b.txt"]);
        Assert.NotNull(nodeA);
        Assert.NotNull(nodeB);
        
        nodeA.CheckState = CheckState.Checked;
        nodeB.CheckState = CheckState.Checked;

        var registry = new FileSystemProviderRegistry();
        registry.Register(sourceProvider);
        registry.Register(targetProviderA);
        registry.Register(targetProviderB);

        var pipeline = new TransformPipeline([
            new CopyStep("mem://targetA/destA"),
            new CopyStep("mem://targetB/destB")
        ]);

        var runner = new PipelineRunner(pipeline);
        var job = new PipelineJob
        {
            RootNode       = root,
            SourceProvider = sourceProvider,
            ProviderRegistry = registry,
        };

        var results = await runner.ExecuteAsync(job);

        Assert.All(results, r => Assert.True(r.IsSuccess));

        Assert.True(await targetProviderA.ExistsAsync("/destA/source/a.txt", CancellationToken.None));
        Assert.True(await targetProviderA.ExistsAsync("/destA/source/b.txt", CancellationToken.None));
        Assert.True(await targetProviderB.ExistsAsync("/destB/source/a.txt", CancellationToken.None));
        Assert.True(await targetProviderB.ExistsAsync("/destB/source/b.txt", CancellationToken.None));
    }

    [Fact]
    public async Task CrossProviderPipeline_RoutesToCorrectProviders_Preview()
    {
        var sourceProvider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/source")
            .WithFile("/source/a.txt", "x"u8));

        var targetProviderA = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/destA"), customRootPath: "mem://targetA");

        var targetProviderB = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/destB"), customRootPath: "mem://targetB");

        var root = await sourceProvider.BuildDirectoryTree();
        var node = root.FindNodeByPathSegments(["source", "a.txt"]);
        Assert.NotNull(node);
        node.CheckState = CheckState.Checked;

        var registry = new FileSystemProviderRegistry();
        registry.Register(sourceProvider);
        registry.Register(targetProviderA);
        registry.Register(targetProviderB);

        var pipeline = new TransformPipeline([
            new CopyStep("mem://targetA/destA"),
            new CopyStep("mem://targetB/destB")
        ]);

        var runner = new PipelineRunner(pipeline);
        var job = new PipelineJob
        {
            RootNode       = root,
            SourceProvider = sourceProvider,
            ProviderRegistry = registry,
        };

        var plan = await runner.PreviewAsync(job, CancellationToken.None);

        Assert.Equal(2, plan.Actions.Count);
        
        var actionA = plan.Actions[0];
        Assert.Equal(SourceResult.Copied, actionA.SourceResult);
        Assert.Equal("mem://targetA/destA/source/a.txt", actionA.DestinationPath);

        var actionB = plan.Actions[1];
        Assert.Equal(SourceResult.Copied, actionB.SourceResult);
        Assert.Equal("mem://targetB/destB/source/a.txt", actionB.DestinationPath);
    }
}
