using System.IO;
using System.Linq;
using System.Text;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Tests.TestInfrastructure;
using SmartCopy.UI.ViewModels;

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
        var pipeline = new TransformPipeline([new CopyStep("/mem/Mirror")]);
        var runner = new PipelineRunner(pipeline);

        var job = new PipelineJob
        {
            RootNode       = sourceRoot,
            SourceProvider = provider,
            ProviderRegistry = provider.CreateRegistry(),
            OverwriteMode  = OverwriteMode.IfNewer,
            DeleteMode     = DeleteMode.Trash,
        };

        var plan = await runner.PreviewAsync(job, CancellationToken.None);

        Assert.Single(plan.Actions);
        Assert.Contains("/Mirror", plan.Actions[0].DestinationPath);

        var results = await runner.ExecuteAsync(job, progress: null, ct: CancellationToken.None);

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
            OverwriteMode  = OverwriteMode.Always,
            DeleteMode     = DeleteMode.Permanent,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.ExecuteAsync(job, progress: null, ct: CancellationToken.None));
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
            OverwriteMode  = OverwriteMode.Always,
            DeleteMode     = DeleteMode.Permanent,
        };

        await runner.PreviewAsync(job, ct: CancellationToken.None);
        await runner.ExecuteAsync(job, progress: null, ct: CancellationToken.None);

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

        DirectoryTreeNode root = await provider.BuildDirectoryTree();

        var sourceNode = root.FindNodeByPathSegments(["source"]);
        Assert.NotNull(sourceNode);
        sourceNode.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new DeleteStep()]));
        var registry = provider.CreateRegistry();

        var job = new PipelineJob
        {
            RootNode       = sourceNode,
            SourceProvider = provider,
            ProviderRegistry = registry,
            OverwriteMode  = OverwriteMode.Always,
            DeleteMode     = DeleteMode.Permanent,
        };

        var plan = await runner.PreviewAsync(job, CancellationToken.None);

        Assert.Equal(5, plan.Actions.Count);
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
            new CopyStep("/mem/out"),
        ]));

        await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = sourceRoot,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode  = OverwriteMode.Always,
                DeleteMode     = DeleteMode.Trash,
            },
            progress: null,
            ct: CancellationToken.None);

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
            new CopyStep("/mem/out"),
        ]));

        var plan = await runner.PreviewAsync(
            new PipelineJob
            {
                RootNode       = sourceRoot,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode  = OverwriteMode.Always,
                DeleteMode     = DeleteMode.Trash,
            },
            CancellationToken.None);

        var copyAction = plan.Actions.Single(a => a.SourceResult == SourceResult.Copied);
        Assert.Equal("/mem/out/track.mp3", copyAction.DestinationPath);
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
        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("/mem/dest")]));

        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = sourceRoot,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode  = OverwriteMode.Skip,
                DeleteMode     = DeleteMode.Trash,
            },
            progress: null,
            ct: CancellationToken.None);

        Assert.Contains(results, r => r.SourceNodeResult == SourceResult.None);
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
        var runner = new PipelineRunner(new TransformPipeline([new CopyStep("/mem/dest")]));

        var emptyJob = new PipelineJob
        {
            RootNode       = root,
            SourceProvider = provider,
            ProviderRegistry = provider.CreateRegistry(),
            OverwriteMode  = OverwriteMode.Always,
            DeleteMode     = DeleteMode.Trash,
        };

        var previewError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.PreviewAsync(emptyJob, CancellationToken.None));

        Assert.Contains("At least one file must be selected", previewError.Message);

        var executeError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.ExecuteAsync(emptyJob, progress: null, ct: CancellationToken.None));

        Assert.Contains("At least one file must be selected", executeError.Message);
    }
}
