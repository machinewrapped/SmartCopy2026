using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Core.Progress;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

public sealed class PipelineIntegrationTests
{
    [Fact]
    public async Task CopyPipeline_WritesExpectedOutput()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/src")
            .WithDirectory("/dest")
            .WithFile("/src/song.mp3", "audio"u8));

        var root = await provider.BuildDirectoryTree();
        var node = root.FindNodeByPathSegments(["src", "song.mp3"]);
        Assert.NotNull(node);

        node.CheckState = CheckState.Checked;
        var runner = new PipelineRunner(new TransformPipeline([new CopyStep("/mem/dest")]));

        await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = root,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode  = OverwriteMode.Always,
            });

        Assert.True(await provider.ExistsAsync("/dest/src/song.mp3", CancellationToken.None));
    }

    [Fact]
    public async Task FlattenCopyPipeline_ProducesFlatOutput()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/src/deep")
            .WithDirectory("/dest")
            .WithFile("/src/deep/song.mp3", "audio"u8));

        var root = await provider.BuildDirectoryTree();
        var node = root.FindNodeByPathSegments(["src", "deep", "song.mp3"]);
        Assert.NotNull(node);
        node.CheckState = CheckState.Checked;
        var runner = new PipelineRunner(new TransformPipeline(
        [
            new FlattenStep(),
            new CopyStep("/mem/dest"),
        ]));

        await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = root,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode  = OverwriteMode.Always,
            });

        Assert.True(await provider.ExistsAsync("/dest/song.mp3", CancellationToken.None));
    }

    [Fact]
    public async Task DeletePipeline_EnforcesMandatoryPreview()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/src")
            .WithFile("/src/delete.txt", "x"u8));

        var root = await provider.BuildDirectoryTree();
        var node = root.FindNodeByPathSegments(["src", "delete.txt"]);
        Assert.NotNull(node);
        node.CheckState = CheckState.Checked;
        var runner = new PipelineRunner(new TransformPipeline([new DeleteStep()]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.ExecuteAsync(
                new PipelineJob
                {
                    RootNode       = root,
                    SourceProvider = provider,
                    ProviderRegistry = provider.CreateRegistry(),
                    OverwriteMode  = OverwriteMode.Always,
                }));
    }

    [Fact]
    public async Task CopyPipeline_HonorsOverwriteSkipAndAlways()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/src")
            .WithDirectory("/dest/src")
            .WithFile("/src/song.mp3", "new"u8)
            .WithFile("/dest/src/song.mp3", "old"u8));

        var root = await provider.BuildDirectoryTree();
        var node = root.FindNodeByPathSegments(["src", "song.mp3"]);
        Assert.NotNull(node);
        node.CheckState = CheckState.Checked;
        var runner = new PipelineRunner(new TransformPipeline([new CopyStep("/mem/dest")]));

        var skipResults = await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode = root,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode = OverwriteMode.Skip,
            });

        Assert.Contains(skipResults, result => result.SourceNodeResult == SourceResult.None);

        var alwaysResults = await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = root,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode  = OverwriteMode.Always,
            });

        Assert.Contains(alwaysResults, result => result.SourceNodeResult == SourceResult.Copied);
    }

    [Fact]
    public async Task CopyThenMovePipeline_ExecutesInOrder()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/src")
            .WithDirectory("/backup")
            .WithDirectory("/archive")
            .WithFile("/src/song.mp3", "audio"u8));

        var root = await provider.BuildDirectoryTree();
        var node = root.FindNodeByPathSegments(["src", "song.mp3"]);
        Assert.NotNull(node);
        node.CheckState = CheckState.Checked;
        var runner = new PipelineRunner(new TransformPipeline(
        [
            new CopyStep("/mem/backup"),
            new MoveStep("/mem/archive"),
        ]));

        await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = root,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode  = OverwriteMode.Always,
            });

        Assert.True(await provider.ExistsAsync("/backup/src/song.mp3", CancellationToken.None));
        Assert.True(await provider.ExistsAsync("/archive/src/song.mp3", CancellationToken.None));
        Assert.False(await provider.ExistsAsync("/src/song.mp3", CancellationToken.None));
    }

    [Fact]
    public async Task OperationJournal_IsWrittenWithParseableEntries()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithDirectory("/src")
            .WithDirectory("/dest")
            .WithSimulatedFile("/src/song.mp3", 256*1024));

        var root = await provider.BuildDirectoryTree();
        var node = root.FindNodeByPathSegments(["src", "song.mp3"]);
        Assert.NotNull(node);
        node.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new CopyStep("/mem/dest")]));

        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = root,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode  = OverwriteMode.Always,
            });

        var logDir = Path.Combine(Path.GetTempPath(), "SmartCopy2026.Tests", Guid.NewGuid().ToString("N"), "logs");
        var journal = new OperationJournal(logDir);
        var path = await journal.WriteAsync(results.Where(r => r.SourceNodeResult != SourceResult.None));

        Assert.True(File.Exists(path));
        var line = Assert.Single(await File.ReadAllLinesAsync(path));
        var columns = line.Split('\t');
        Assert.Equal(6, columns.Length);
        Assert.Equal("ok", columns[1]);
        Assert.Equal("copy", columns[2]);
        Assert.Equal("src/song.mp3", columns[3]);
        Assert.Equal("/mem/dest/src/song.mp3", columns[4]);
        Assert.Equal("256.0KB", columns[5]);
    }
}
