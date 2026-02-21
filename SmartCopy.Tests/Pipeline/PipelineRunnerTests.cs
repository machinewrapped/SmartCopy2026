using System.IO;
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
    public async Task DeletePipeline_RequiresPreviewBeforeExecute()
    {
        var provider = new MemoryFileSystemProvider();
        provider.SeedDirectory("/source");
        provider.SeedFile("/source/delete-me.txt", "x"u8);

        var node = await provider.GetNodeAsync("/source/delete-me.txt", CancellationToken.None);
        var runner = new PipelineRunner(new TransformPipeline([new DeleteStep()]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.ExecuteAsync(
                [node],
                provider,
                targetProvider: null,
                overwriteMode: OverwriteMode.Always,
                deleteMode: DeleteMode.Permanent,
                progress: null,
                ct: CancellationToken.None));
    }

    [Fact]
    public async Task DeletePipeline_RemovesSourceFile_AfterPreview()
    {
        var provider = new MemoryFileSystemProvider();
        provider.SeedDirectory("/source");
        provider.SeedFile("/source/delete-me.txt", "x"u8);

        var node = await provider.GetNodeAsync("/source/delete-me.txt", CancellationToken.None);
        var runner = new PipelineRunner(new TransformPipeline([new DeleteStep()]));

        await runner.PreviewAsync(
            [node],
            provider,
            targetProvider: null,
            overwriteMode: OverwriteMode.Always,
            deleteMode: DeleteMode.Permanent,
            ct: CancellationToken.None);

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

    [Fact]
    public async Task FlattenThenCopy_PreviewReportsFlattened_DestinationPath()
    {
        var sourceProvider = new MemoryFileSystemProvider();
        var targetProvider = new MemoryFileSystemProvider();

        sourceProvider.SeedDirectory("/source/deep/folder");
        sourceProvider.SeedFile("/source/deep/folder/track.mp3", "x"u8);
        targetProvider.SeedDirectory("/out");

        var node = await sourceProvider.GetNodeAsync("/source/deep/folder/track.mp3", CancellationToken.None);
        var runner = new PipelineRunner(new TransformPipeline(
        [
            new FlattenStep(),
            new CopyStep("/out"),
        ]));

        var plan = await runner.PreviewAsync(
            [node],
            sourceProvider,
            targetProvider,
            OverwriteMode.Always,
            DeleteMode.Trash,
            CancellationToken.None);

        var copyAction = plan.Actions.Single(a => a.StepSummary == "Copy");
        Assert.Equal(Path.Combine("/out", "track.mp3"), copyAction.DestinationPath);
    }

    [Fact]
    public async Task MoveStep_SkipsExistingDestination_WhenOverwriteModeIsSkip()
    {
        var provider = new MemoryFileSystemProvider();
        provider.SeedDirectory("/source");
        provider.SeedDirectory("/dest");
        provider.SeedFile("/source/song.mp3", "original"u8);
        provider.SeedFile("/dest/source/song.mp3", "existing"u8);

        var node = await provider.GetNodeAsync("/source/song.mp3", CancellationToken.None);
        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("/dest")]));

        var results = await runner.ExecuteAsync(
            [node],
            provider,
            targetProvider: provider,
            overwriteMode: OverwriteMode.Skip,
            deleteMode: DeleteMode.Trash,
            progress: null,
            ct: CancellationToken.None);

        Assert.Contains(results, r => r.Message == "Skipped existing destination.");
        // Source must not have been deleted.
        Assert.True(await provider.ExistsAsync("/source/song.mp3", CancellationToken.None));
        // Destination must remain unchanged.
        await using var stream = await provider.OpenReadAsync("/dest/source/song.mp3", CancellationToken.None);
        var bytes = new byte[stream.Length];
        _ = await stream.ReadAsync(bytes, CancellationToken.None);
        Assert.Equal("existing"u8.ToArray(), bytes);
    }
}
