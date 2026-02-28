using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Filters;
using SmartCopy.Core.Filters.Filters;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

public sealed class PipelineDirectoryTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static FileSystemNode MakeDirNode(string name) =>
        new() { Name = name, FullPath = "/" + name, RelativePathSegments = [name], IsDirectory = true };

    // ─────────────────────────────────────────────────────────────────────────
    // FilterResult computation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FilterResult_Mixed_WhenSomeChildrenExcluded()
    {
        var dir = MakeDirNode("music");
        var mp3 = new FileSystemNode
        {
            Name = "a.mp3", FullPath = "/music/a.mp3",
            RelativePathSegments = ["music", "a.mp3"], IsDirectory = false, Size = 100, Parent = dir,
        };
        var txt = new FileSystemNode
        {
            Name = "b.txt", FullPath = "/music/b.txt",
            RelativePathSegments = ["music", "b.txt"], IsDirectory = false, Size = 50, Parent = dir,
        };
        dir.Files.Add(mp3);
        dir.Files.Add(txt);

        await new FilterChain([new ExtensionFilter(["mp3"], FilterMode.Only)]).ApplyToTreeAsync([dir]);

        Assert.Equal(FilterResult.Mixed, dir.FilterResult);
        Assert.True(dir.IsFilterIncluded);   // Mixed → visible in tree (not Excluded)
        Assert.False(dir.IsAtomicIncluded);  // Mixed → NOT safe for atomic operations
    }

    [Fact]
    public async Task FilterResult_Included_WhenAllChildrenIncluded()
    {
        var dir = MakeDirNode("music");
        var f1 = new FileSystemNode
        {
            Name = "a.mp3", FullPath = "/music/a.mp3",
            RelativePathSegments = ["music", "a.mp3"], IsDirectory = false, Size = 100, Parent = dir,
        };
        var f2 = new FileSystemNode
        {
            Name = "b.mp3", FullPath = "/music/b.mp3",
            RelativePathSegments = ["music", "b.mp3"], IsDirectory = false, Size = 200, Parent = dir,
        };
        dir.Files.Add(f1);
        dir.Files.Add(f2);

        await new FilterChain([]).ApplyToTreeAsync([dir]); // empty chain → all Included

        Assert.Equal(FilterResult.Included, dir.FilterResult);
        Assert.True(dir.IsFilterIncluded);
        Assert.True(dir.IsAtomicIncluded);
    }

    [Fact]
    public void IsSelected_TrueForDirectoryWhenFullyCovered()
    {
        var dir = MakeDirNode("music");
        dir.FilterResult = FilterResult.Included;
        dir.CheckState = CheckState.Checked;

        Assert.True(dir.IsSelected);
        Assert.True(dir.IsAtomicIncluded);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Move: atomic directory (same provider)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Move_AtomicDir_WhenFullyCoveredSameProvider()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/src/music/a.flac", 1000)
            .WithSimulatedFile("/src/music/b.flac", 2000)
            .WithDirectory("/dest"));
        var ct = CancellationToken.None;
        var dirNode = await provider.GetNodeAsync("/src/music", ct);
        dirNode.FilterResult = FilterResult.Included;
        dirNode.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("/dest")]));
        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                FilterIncludedFiles = [dirNode],
                SelectedFiles       = [dirNode],
                SourceProvider      = provider,
                TargetProvider      = provider,
                OverwriteMode       = OverwriteMode.Always,
                DeleteMode          = DeleteMode.Trash,
            },
            progress: null,
            ct: ct);

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        Assert.Equal(SourcePathResult.Moved, results[0].SourcePathResult);
        Assert.False(await provider.ExistsAsync("/src/music", ct));
        Assert.True(await provider.ExistsAsync("/dest/src/music", ct));
        Assert.True(await provider.ExistsAsync("/dest/src/music/a.flac", ct));
        Assert.True(await provider.ExistsAsync("/dest/src/music/b.flac", ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Delete: atomic directory
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_AtomicDir_WhenFullyCovered()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/src/music/a.flac", 500)
            .WithSimulatedFile("/src/music/b.flac", 800));
        var ct = CancellationToken.None;
        var dirNode = await provider.GetNodeAsync("/src/music", ct);
        dirNode.FilterResult = FilterResult.Included;
        dirNode.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new DeleteStep()]));
        var job = new PipelineJob
        {
            FilterIncludedFiles = [dirNode],
            SelectedFiles       = [dirNode],
            SourceProvider      = provider,
            TargetProvider      = null,
            OverwriteMode       = OverwriteMode.Always,
            DeleteMode          = DeleteMode.Permanent,
        };

        await runner.PreviewAsync(job, ct);
        var results = await runner.ExecuteAsync(job, progress: null, ct: ct);

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        Assert.False(await provider.ExistsAsync("/src/music", ct));
        Assert.False(await provider.ExistsAsync("/src/music/a.flac", ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Move: partial coverage — processes individual files, parent stays
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Move_PartialCoverage_ProcessesFilesIndividually()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/src/music/a.mp3", 500)
            .WithFile("/src/music/readme.txt", "notes"u8)
            .WithDirectory("/dest"));
        var ct = CancellationToken.None;
        // Dir is Mixed (readme excluded) → not atomic → only the included file in SelectedFiles
        var mp3Node = await provider.GetNodeAsync("/src/music/a.mp3", ct);
        mp3Node.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("/dest")]));
        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                FilterIncludedFiles = [mp3Node],
                SelectedFiles       = [mp3Node],
                SourceProvider      = provider,
                TargetProvider      = provider,
                OverwriteMode       = OverwriteMode.Always,
                DeleteMode          = DeleteMode.Trash,
            },
            progress: null,
            ct: ct);

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        // mp3 was moved
        Assert.False(await provider.ExistsAsync("/src/music/a.mp3", ct));
        Assert.True(await provider.ExistsAsync("/dest/src/music/a.mp3", ct));
        // excluded readme.txt keeps parent dir non-empty
        Assert.True(await provider.ExistsAsync("/src/music/readme.txt", ct));
        Assert.True(await provider.ExistsAsync("/src/music", ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Move: cross-provider dir fails gracefully
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Move_NoAtomicDir_WhenDifferentProviders()
    {
        var (sourceProvider, targetProvider) = MemoryFileSystemFixtures.CreatePair(
            src => src.WithSimulatedFile("/src/music/a.flac", 1000),
            tgt => tgt.WithDirectory("/dest"));
        var ct = CancellationToken.None;
        var dirNode = await sourceProvider.GetNodeAsync("/src/music", ct);
        dirNode.FilterResult = FilterResult.Included;
        dirNode.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("/dest")]));
        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                FilterIncludedFiles = [dirNode],
                SelectedFiles       = [dirNode],
                SourceProvider      = sourceProvider,
                TargetProvider      = targetProvider,
                OverwriteMode       = OverwriteMode.Always,
                DeleteMode          = DeleteMode.Trash,
            },
            progress: null,
            ct: ct);

        Assert.Single(results);
        Assert.False(results[0].IsSuccess);
        Assert.True(await sourceProvider.ExistsAsync("/src/music", ct)); // source untouched
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Move: directory honours path transforms
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Move_DirectoryNodeRespectsFlattenedPath()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/src/music/rock/a.flac", 1000)
            .WithDirectory("/dest"));
        var ct = CancellationToken.None;
        var dirNode = await provider.GetNodeAsync("/src/music/rock", ct);
        dirNode.FilterResult = FilterResult.Included;
        dirNode.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline(
        [
            new FlattenStep(),
            new MoveStep("/dest"),
        ]));
        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                FilterIncludedFiles = [dirNode],
                SelectedFiles       = [dirNode],
                SourceProvider      = provider,
                TargetProvider      = provider,
                OverwriteMode       = OverwriteMode.Always,
                DeleteMode          = DeleteMode.Trash,
            },
            progress: null,
            ct: ct);

        // FlattenStep + MoveStep each produce a result
        Assert.Equal(2, results.Count);
        var moveResult = results.Single(r => r.SourcePathResult == SourcePathResult.Moved);
        Assert.True(moveResult.IsSuccess);
        // Flattened: ["src","music","rock"] → ["rock"] → destination /dest/rock
        Assert.True(await provider.ExistsAsync("/dest/rock", ct));
        Assert.True(await provider.ExistsAsync("/dest/rock/a.flac", ct));
        Assert.False(await provider.ExistsAsync("/src/music/rock", ct));
    }

    [Fact]
    public async Task Move_DirectoryNodeRespectsRebasedPath()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/src/music/a.flac", 1000)
            .WithDirectory("/dest"));
        var ct = CancellationToken.None;
        var dirNode = await provider.GetNodeAsync("/src/music", ct);
        dirNode.FilterResult = FilterResult.Included;
        dirNode.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline(
        [
            new RebaseStep(stripPrefix: "src", addPrefix: "archive"),
            new MoveStep("/dest"),
        ]));
        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                FilterIncludedFiles = [dirNode],
                SelectedFiles       = [dirNode],
                SourceProvider      = provider,
                TargetProvider      = provider,
                OverwriteMode       = OverwriteMode.Always,
                DeleteMode          = DeleteMode.Trash,
            },
            progress: null,
            ct: ct);

        // RebaseStep + MoveStep each produce a result
        Assert.Equal(2, results.Count);
        var moveResult = results.Single(r => r.SourcePathResult == SourcePathResult.Moved);
        Assert.True(moveResult.IsSuccess);
        // strip "src", add "archive" → ["archive","music"] → /dest/archive/music
        Assert.True(await provider.ExistsAsync("/dest/archive/music", ct));
        Assert.True(await provider.ExistsAsync("/dest/archive/music/a.flac", ct));
        Assert.False(await provider.ExistsAsync("/src/music", ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Move: only topmost fully-covered dir in SelectedFiles
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Move_NestedFullyCoveredDirs_UseTopmost()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/src/music/rock/a.flac", 1000)
            .WithSimulatedFile("/src/music/rock/b.flac", 2000)
            .WithDirectory("/dest"));
        var ct = CancellationToken.None;
        // Only the topmost parent dir goes into SelectedFiles (as CollectSelectedNodesRecursive would produce)
        var parentDir = await provider.GetNodeAsync("/src/music", ct);
        parentDir.FilterResult = FilterResult.Included;
        parentDir.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("/dest")]));
        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                FilterIncludedFiles = [parentDir],
                SelectedFiles       = [parentDir], // NOT the nested rock dir separately
                SourceProvider      = provider,
                TargetProvider      = provider,
                OverwriteMode       = OverwriteMode.Always,
                DeleteMode          = DeleteMode.Trash,
            },
            progress: null,
            ct: ct);

        // One atomic result covering the entire subtree including nested dirs
        Assert.Single(results);
        Assert.Equal(SourcePathResult.Moved, results[0].SourcePathResult);
        Assert.False(await provider.ExistsAsync("/src/music", ct));
        Assert.True(await provider.ExistsAsync("/dest/src/music/rock/a.flac", ct));
        Assert.True(await provider.ExistsAsync("/dest/src/music/rock/b.flac", ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Delete: excluded child keeps parent alive
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_AtomicDir_ExcludedChildKeepsParent()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/src/music/rock/a.flac", 1000)
            .WithFile("/src/music/excluded.txt", "x"u8));
        var ct = CancellationToken.None;
        // Parent is Mixed: has a selected subdir AND an excluded file at parent level.
        // Only the rock subdir is processed; excluded.txt keeps /src/music non-empty.
        var rockDir = await provider.GetNodeAsync("/src/music/rock", ct);
        rockDir.FilterResult = FilterResult.Included;
        rockDir.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new DeleteStep()]));
        var job = new PipelineJob
        {
            FilterIncludedFiles = [rockDir],
            SelectedFiles       = [rockDir],
            SourceProvider      = provider,
            TargetProvider      = null,
            OverwriteMode       = OverwriteMode.Always,
            DeleteMode          = DeleteMode.Permanent,
        };
        await runner.PreviewAsync(job, ct);
        var results = await runner.ExecuteAsync(job, progress: null, ct: ct);

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        Assert.False(await provider.ExistsAsync("/src/music/rock", ct));       // rock subdir gone
        Assert.True(await provider.ExistsAsync("/src/music/excluded.txt", ct)); // excluded file stays
        Assert.True(await provider.ExistsAsync("/src/music", ct));              // parent still exists
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CollectSelectedNodes: dir node represents subtree (not individual children)
    // ─────────────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────────────
    // Copy: preview reports correct file count for directory nodes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Copy_Preview_ExpandsDirectoryToPerFileActions()
    {
        var (sourceProvider, targetProvider) = MemoryFileSystemFixtures.CreatePair(
            src => src
                .WithSimulatedFile("/src/music/a.flac", 1000)
                .WithSimulatedFile("/src/music/b.flac", 2000),
            tgt => tgt
                .WithDirectory("/dest")
                .WithSimulatedFile("/dest/src/music/a.flac", 1000)); // a.flac already exists at dest
        var ct = CancellationToken.None;
        var dirNode = await sourceProvider.GetNodeAsync("/src/music", ct);
        dirNode.FilterResult = FilterResult.Included;
        dirNode.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new CopyStep("/dest")]));
        var plan = await runner.PreviewAsync(
            new PipelineJob
            {
                FilterIncludedFiles = [dirNode],
                SelectedFiles       = [dirNode],
                SourceProvider      = sourceProvider,
                TargetProvider      = targetProvider,
                OverwriteMode       = OverwriteMode.Always,
                DeleteMode          = DeleteMode.Trash,
            }, ct);

        // One action per file, not one per directory
        Assert.Equal(2, plan.Actions.Count);
        Assert.Equal(2, plan.TotalFilesAffected);

        // a.flac already exists → Overwritten; b.flac is new → Created
        var aAction = plan.Actions.Single(a => a.SourcePath.EndsWith("a.flac"));
        var bAction = plan.Actions.Single(a => a.SourcePath.EndsWith("b.flac"));
        Assert.Equal(DestinationPathResult.Overwritten, aAction.DestinationPathResult);
        Assert.Equal(DestinationPathResult.Created, bAction.DestinationPathResult);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CollectSelectedNodes: dir node represents subtree (not individual children)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectSelectedNodes_ReturnsDirNode_NotChildren()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/src/music/a.flac", 1000)
            .WithSimulatedFile("/src/music/b.flac", 2000)
            .WithDirectory("/dest"));
        var ct = CancellationToken.None;
        var dirNode = await provider.GetNodeAsync("/src/music", ct);
        dirNode.FilterResult = FilterResult.Included;
        dirNode.CheckState = CheckState.Checked;

        // CollectSelectedNodesRecursive adds the dir node (not individual files).
        // With [dirNode] in SelectedFiles the pipeline sees exactly 1 item → 1 atomic result.
        // If individual files were in SelectedFiles instead, there would be 2 results.
        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("/dest")]));
        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                FilterIncludedFiles = [dirNode],
                SelectedFiles       = [dirNode],
                SourceProvider      = provider,
                TargetProvider      = provider,
                OverwriteMode       = OverwriteMode.Always,
                DeleteMode          = DeleteMode.Trash,
            },
            progress: null,
            ct: ct);

        Assert.Single(results);
        Assert.Equal(SourcePathResult.Moved, results[0].SourcePathResult);
    }
}
