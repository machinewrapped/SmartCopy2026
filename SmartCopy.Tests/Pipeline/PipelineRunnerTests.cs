using System.Linq;
using System.Text;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.Tests.Pipeline;

public sealed class PipelineRunnerTests
{
    [Fact]
    public async Task CopyPipeline_PreviewsAndExecutes()
    {
        var sourceProvider = new MemoryFileSystemProvider();
        var targetProvider = new MemoryFileSystemProvider();

        sourceProvider.SeedDirectory("/source");
        sourceProvider.SeedFile("/source/song.flac", Encoding.UTF8.GetBytes("audio"));
        targetProvider.SeedDirectory("/target");

        var sourceNode = await sourceProvider.GetNodeAsync("/source/song.flac", CancellationToken.None);
        var pipeline = new TransformPipeline([new CopyStep("/target")]);
        var runner = new PipelineRunner(pipeline);

        var plan = await runner.PreviewAsync(
            [sourceNode],
            sourceProvider,
            targetProvider,
            OverwriteMode.IfNewer,
            DeleteMode.Trash,
            CancellationToken.None);

        Assert.Single(plan.Actions);
        Assert.Contains("/target", plan.Actions[0].DestinationPath);

        var results = await runner.ExecuteAsync(
            [sourceNode],
            sourceProvider,
            targetProvider,
            OverwriteMode.IfNewer,
            DeleteMode.Trash,
            progress: null,
            CancellationToken.None);

        Assert.Contains(results, r => r.StepType == "Copy" && r.Success);
        Assert.True(await targetProvider.ExistsAsync("/target/source/song.flac", CancellationToken.None));
    }

    [Fact]
    public async Task DeletePipeline_RemovesSourceFile()
    {
        var provider = new MemoryFileSystemProvider();
        provider.SeedDirectory("/source");
        provider.SeedFile("/source/delete-me.txt", "x"u8);

        var node = await provider.GetNodeAsync("/source/delete-me.txt", CancellationToken.None);
        var runner = new PipelineRunner(new TransformPipeline([new DeleteStep()]));

        await runner.ExecuteAsync(
            [node],
            provider,
            targetProvider: null,
            overwriteMode: OverwriteMode.Always,
            deleteMode: DeleteMode.Permanent,
            progress: null,
            ct: CancellationToken.None);

        Assert.False(await provider.ExistsAsync("/source/delete-me.txt", CancellationToken.None));
    }

    [Fact]
    public async Task FlattenThenCopy_FlattensPathAtDestination()
    {
        var sourceProvider = new MemoryFileSystemProvider();
        var targetProvider = new MemoryFileSystemProvider();

        sourceProvider.SeedDirectory("/source");
        sourceProvider.SeedDirectory("/source/deep");
        sourceProvider.SeedDirectory("/source/deep/folder");
        sourceProvider.SeedFile("/source/deep/folder/track.mp3", "x"u8);
        targetProvider.SeedDirectory("/out");

        var node = await sourceProvider.GetNodeAsync("/source/deep/folder/track.mp3", CancellationToken.None);
        var runner = new PipelineRunner(new TransformPipeline(
        [
            new FlattenStep(),
            new CopyStep("/out"),
        ]));

        await runner.ExecuteAsync(
            [node],
            sourceProvider,
            targetProvider,
            OverwriteMode.Always,
            DeleteMode.Trash,
            progress: null,
            ct: CancellationToken.None);

        Assert.True(await targetProvider.ExistsAsync("/out/track.mp3", CancellationToken.None));
    }
}

