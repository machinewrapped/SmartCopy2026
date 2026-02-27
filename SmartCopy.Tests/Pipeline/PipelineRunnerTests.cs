using System.IO;
using System.Linq;
using System.Text;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

public sealed class PipelineRunnerTests
{
    [Fact]
    public async Task CopyPipeline_PreviewsAndExecutes()
    {
        var (sourceProvider, targetProvider) = MemoryFileSystemFixtures.CreatePair(
            source => source
                .WithDirectory("/source")
                .WithFile("/source/song.flac", Encoding.UTF8.GetBytes("audio")),
            target => target.WithDirectory("/Mirror"));

        var sourceNode = await sourceProvider.GetNodeAsync("/source/song.flac", CancellationToken.None);
        sourceNode.CheckState = CheckState.Checked;
        var pipeline = new TransformPipeline([new CopyStep("/Mirror")]);
        var runner = new PipelineRunner(pipeline);

        var job = new PipelineJob
        {
            FilterIncludedFiles = [sourceNode],
            SelectedFiles       = [sourceNode],
            SourceProvider      = sourceProvider,
            TargetProvider      = targetProvider,
            OverwriteMode       = OverwriteMode.IfNewer,
            DeleteMode          = DeleteMode.Trash,
        };

        var plan = await runner.PreviewAsync(job, CancellationToken.None);

        Assert.Single(plan.Actions);
        Assert.Contains("/Mirror", plan.Actions[0].DestinationPath);

        var results = await runner.ExecuteAsync(job, progress: null, CancellationToken.None);

        Assert.Contains(results, r => r.StepType == StepKind.Copy && r.Success);
        Assert.True(await targetProvider.ExistsAsync("/Mirror/source/song.flac", CancellationToken.None));
    }

    [Fact]
    public async Task DeletePipeline_RequiresPreviewBeforeExecute()
    {
        var provider = MemoryFileSystemFixtures.Create(fixture => fixture
            .WithDirectory("/source")
            .WithFile("/source/delete-me.txt", "x"u8));

        var node = await provider.GetNodeAsync("/source/delete-me.txt", CancellationToken.None);
        node.CheckState = CheckState.Checked;
        var runner = new PipelineRunner(new TransformPipeline([new DeleteStep()]));

        var job = new PipelineJob
        {
            FilterIncludedFiles = [node],
            SelectedFiles       = [node],
            SourceProvider      = provider,
            TargetProvider      = null,
            OverwriteMode       = OverwriteMode.Always,
            DeleteMode          = DeleteMode.Permanent,
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

        var node = await provider.GetNodeAsync("/source/delete-me.txt", CancellationToken.None);
        node.CheckState = CheckState.Checked;
        var runner = new PipelineRunner(new TransformPipeline([new DeleteStep()]));

        var job = new PipelineJob
        {
            FilterIncludedFiles = [node],
            SelectedFiles       = [node],
            SourceProvider      = provider,
            TargetProvider      = null,
            OverwriteMode       = OverwriteMode.Always,
            DeleteMode          = DeleteMode.Permanent,
        };

        await runner.PreviewAsync(job, ct: CancellationToken.None);
        await runner.ExecuteAsync(job, progress: null, ct: CancellationToken.None);

        Assert.False(await provider.ExistsAsync("/source/delete-me.txt", CancellationToken.None));
    }

    [Fact]
    public async Task FlattenThenCopy_FlattensPathAtDestination()
    {
        var (sourceProvider, targetProvider) = MemoryFileSystemFixtures.CreatePair(
            source => source
                .WithDirectory("/source")
                .WithDirectory("/source/deep")
                .WithDirectory("/source/deep/folder")
                .WithFile("/source/deep/folder/track.mp3", "x"u8),
            target => target.WithDirectory("/out"));

        var node = await sourceProvider.GetNodeAsync("/source/deep/folder/track.mp3", CancellationToken.None);
        node.CheckState = CheckState.Checked;
        var runner = new PipelineRunner(new TransformPipeline(
        [
            new FlattenStep(),
            new CopyStep("/out"),
        ]));

        await runner.ExecuteAsync(
            new PipelineJob
            {
                FilterIncludedFiles = [node],
                SelectedFiles       = [node],
                SourceProvider      = sourceProvider,
                TargetProvider      = targetProvider,
                OverwriteMode       = OverwriteMode.Always,
                DeleteMode          = DeleteMode.Trash,
            },
            progress: null,
            ct: CancellationToken.None);

        Assert.True(await targetProvider.ExistsAsync("/out/track.mp3", CancellationToken.None));
    }

    [Fact]
    public async Task FlattenThenCopy_PreviewReportsFlattened_DestinationPath()
    {
        var (sourceProvider, targetProvider) = MemoryFileSystemFixtures.CreatePair(
            source => source
                .WithDirectory("/source/deep/folder")
                .WithFile("/source/deep/folder/track.mp3", "x"u8),
            target => target.WithDirectory("/out"));

        var node = await sourceProvider.GetNodeAsync("/source/deep/folder/track.mp3", CancellationToken.None);
        node.CheckState = CheckState.Checked;
        var runner = new PipelineRunner(new TransformPipeline(
        [
            new FlattenStep(),
            new CopyStep("/out"),
        ]));

        var plan = await runner.PreviewAsync(
            new PipelineJob
            {
                FilterIncludedFiles = [node],
                SelectedFiles       = [node],
                SourceProvider      = sourceProvider,
                TargetProvider      = targetProvider,
                OverwriteMode       = OverwriteMode.Always,
                DeleteMode          = DeleteMode.Trash,
            },
            CancellationToken.None);

        var copyAction = plan.Actions.Single(a => a.StepSummary == "Copy");
        Assert.Equal("/out/track.mp3", copyAction.DestinationPath);
    }

    [Fact]
    public async Task MoveStep_SkipsExistingDestination_WhenOverwriteModeIsSkip()
    {
        var provider = MemoryFileSystemFixtures.Create(fixture => fixture
            .WithDirectory("/source")
            .WithDirectory("/dest")
            .WithFile("/source/song.mp3", "original"u8)
            .WithFile("/dest/source/song.mp3", "existing"u8));

        var node = await provider.GetNodeAsync("/source/song.mp3", CancellationToken.None);
        node.CheckState = CheckState.Checked;
        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("/dest")]));

        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                FilterIncludedFiles = [node],
                SelectedFiles       = [node],
                SourceProvider      = provider,
                TargetProvider      = provider,
                OverwriteMode       = OverwriteMode.Skip,
                DeleteMode          = DeleteMode.Trash,
            },
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

    [Fact]
    public async Task ExecutablePipeline_RequiresAtLeastOneSelectedInput()
    {
        var provider = MemoryFileSystemFixtures.Create(fixture => fixture
            .WithDirectory("/source")
            .WithDirectory("/dest"));
        var runner = new PipelineRunner(new TransformPipeline([new CopyStep("/dest")]));

        var emptyJob = new PipelineJob
        {
            FilterIncludedFiles = [],
            SelectedFiles       = [],
            SourceProvider      = provider,
            TargetProvider      = provider,
            OverwriteMode       = OverwriteMode.Always,
            DeleteMode          = DeleteMode.Trash,
        };

        var previewError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.PreviewAsync(emptyJob, CancellationToken.None));

        Assert.Contains("At least one file must be selected", previewError.Message);

        var executeError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.ExecuteAsync(emptyJob, progress: null, ct: CancellationToken.None));

        Assert.Contains("At least one file must be selected", executeError.Message);
    }
}
