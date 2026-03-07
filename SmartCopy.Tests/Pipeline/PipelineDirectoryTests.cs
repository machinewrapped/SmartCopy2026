using SmartCopy.Core.DirectoryTree;
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
    // FilterResult computation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FilterResult_Mixed_WhenSomeChildrenExcluded()
    {
        DirectoryTreeNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/music")
            .WithSimulatedFile("/music/a.mp3", 100)
            .WithSimulatedFile("/music/b.txt", 50));

        var dir = root.Children.Single(n => n.Name == "music");
        await new FilterChain([new ExtensionFilter(["mp3"], FilterMode.Only)]).ApplyToTreeAsync(root);

        Assert.Equal(FilterResult.Mixed, dir.FilterResult);
        Assert.True(dir.IsFilterIncluded);   // Mixed → visible in tree (not Excluded)
        Assert.False(dir.IsAtomicIncluded);  // Mixed → NOT safe for atomic operations
    }

    [Fact]
    public async Task FilterResult_Included_WhenAllChildrenIncluded()
    {
        DirectoryTreeNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/music")
            .WithSimulatedFile("/music/a.mp3", 100)
            .WithSimulatedFile("/music/b.mp3", 200));

        await new FilterChain([]).ApplyToTreeAsync(root); // empty chain → all Included

        var dir = root.Children.Single(n => n.Name == "music");
        Assert.Equal(FilterResult.Included, dir.FilterResult);
        Assert.True(dir.IsFilterIncluded);
        Assert.True(dir.IsAtomicIncluded);
    }

    [Fact]
    public async Task IsSelected_TrueForDirectoryWhenFullyCovered()
    {
        DirectoryTreeNode root = await MemoryFileSystemFixtures.BuildDirectoryTree(f => f
            .WithDirectory("/music"));

        var dir = root.Children.Single(n => n.Name == "music");
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

        var root = await provider.BuildDirectoryTree();

        var dirNode = root.Children.Single(n => n.Name == "src").Children.Single(n => n.Name == "music");
        dirNode.FilterResult = FilterResult.Included;
        dirNode.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("/mem/dest")]));
        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = root,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode  = OverwriteMode.Always,
            });

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        Assert.Equal(SourceResult.Moved, results[0].SourceNodeResult);
        Assert.False(await provider.ExistsAsync("/src/music", CancellationToken.None));
        Assert.True(await provider.ExistsAsync("/dest/src/music", CancellationToken.None));
        Assert.True(await provider.ExistsAsync("/dest/src/music/a.flac", CancellationToken.None));
        Assert.True(await provider.ExistsAsync("/dest/src/music/b.flac", CancellationToken.None));
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

        // Build the tree from /src/music so music IS the root — avoids accidental root-level deletion.
        var musicRoot = await provider.BuildDirectoryTree("/src/music");
        musicRoot.FilterResult = FilterResult.Included;
        musicRoot.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new DeleteStep()]));
        var job = new PipelineJob
        {
            RootNode       = musicRoot,
            SourceProvider = provider,
            ProviderRegistry = provider.CreateRegistry(),
            OverwriteMode  = OverwriteMode.Always,
        };

        await runner.PreviewAsync(job);
        var results = await runner.ExecuteAsync(job);

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        Assert.False(await provider.ExistsAsync("/src/music", CancellationToken.None));
        Assert.False(await provider.ExistsAsync("/src/music/a.flac", CancellationToken.None));
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

        DirectoryTreeNode root = await provider.BuildDirectoryTree();

        var ct = CancellationToken.None;
        // Only the mp3 is selected — readme.txt is left unchecked (partial coverage).
        var mp3Node = root.FindNodeByPathSegments(["src", "music", "a.mp3"]);
        Assert.NotNull(mp3Node);
        mp3Node.FilterResult = FilterResult.Included;
        mp3Node.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("/mem/dest")]));
        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = root,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode  = OverwriteMode.Always,
            });

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
    // Move: directory honours path transforms
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Move_DirectoryNodeRespectsFlattenedPath()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/src/music/rock/a.flac", 1000)
            .WithDirectory("/dest"));

        // Build from /src/music so "rock" is a direct child of the scan root.
        var musicRoot = await provider.BuildDirectoryTree("/src/music");
        var dirNode = musicRoot.Children.Single(n => n.Name == "rock");
        dirNode.FilterResult = FilterResult.Included;
        dirNode.CheckState = CheckState.Checked;

        var ct = CancellationToken.None;

        var runner = new PipelineRunner(new TransformPipeline(
        [
            new FlattenStep(),
            new MoveStep("/mem/dest"),
        ]));

        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = musicRoot,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode  = OverwriteMode.Always,
            });

        // FlattenStep + MoveStep each produce results for the nodes traversed
        var moveResult = results.Single(r => r.SourceNodeResult == SourceResult.Moved);
        Assert.True(moveResult.IsSuccess);
        // Flattened: ["rock"] → destination /dest/rock
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

        // Build from /src so "music" is a direct child of the scan root.
        var srcRoot = await provider.BuildDirectoryTree("/src");
        var dirNode = srcRoot.Children.Single(n => n.Name == "music");
        dirNode.FilterResult = FilterResult.Included;
        dirNode.CheckState = CheckState.Checked;

        var ct = CancellationToken.None;
        var runner = new PipelineRunner(new TransformPipeline(
        [
            new RebaseStep(stripPrefix: "src", addPrefix: "archive"),
            new MoveStep("/mem/dest"),
        ]));
        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = srcRoot,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode  = OverwriteMode.Always,
            });

        var moveResult = results.Single(r => r.SourceNodeResult == SourceResult.Moved);
        Assert.True(moveResult.IsSuccess);
        // music at scan root → segments ["music"] → add "archive" → ["archive","music"] → /dest/archive/music
        Assert.True(await provider.ExistsAsync("/dest/archive/music", ct));
        Assert.True(await provider.ExistsAsync("/dest/archive/music/a.flac", ct));
        Assert.False(await provider.ExistsAsync("/src/music", ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Move: only topmost fully-covered dir in selection
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Move_NestedFullyCoveredDirs_UseTopmost()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/src/music/rock/a.flac", 1000)
            .WithSimulatedFile("/src/music/rock/b.flac", 2000)
            .WithDirectory("/dest"));

        var root = await provider.BuildDirectoryTree();
        var parentDir = root.FindNodeByPathSegments(["src", "music"]);
        Assert.NotNull(parentDir);
        parentDir.FilterResult = FilterResult.Included;
        parentDir.CheckState = CheckState.Checked;

        var ct = CancellationToken.None;
        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("/mem/dest")]));
        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = root,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode  = OverwriteMode.Always,
            });

        // One atomic result covering the entire subtree including nested dirs
        Assert.Single(results);
        Assert.Equal(SourceResult.Moved, results[0].SourceNodeResult);
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

        // Build from /src/music — music has rock (selected) and excluded.txt (not selected).
        var musicRoot = await provider.BuildDirectoryTree("/src/music");
        var rockDir = musicRoot.Children.Single(n => n.Name == "rock");
        rockDir.FilterResult = FilterResult.Included;
        rockDir.CheckState = CheckState.Checked;

        var runner = new PipelineRunner(new TransformPipeline([new DeleteStep()]));
        var job = new PipelineJob
        {
            RootNode       = musicRoot,
            SourceProvider = provider,
            ProviderRegistry = provider.CreateRegistry(),
            OverwriteMode  = OverwriteMode.Always,
        };
        await runner.PreviewAsync(job);
        var results = await runner.ExecuteAsync(job);

        Assert.Single(results);
        Assert.True(results[0].IsSuccess);
        Assert.False(await provider.ExistsAsync("/src/music/rock", CancellationToken.None));       // rock subdir gone
        Assert.True(await provider.ExistsAsync("/src/music/excluded.txt", CancellationToken.None)); // excluded file stays
        Assert.True(await provider.ExistsAsync("/src/music", CancellationToken.None));              // parent still exists
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Copy: preview reports correct file count for directory nodes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Copy_Preview_ExpandsDirectoryToPerFileActions()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
                .WithSimulatedFile("/src/music/a.flac", 1000)
                .WithSimulatedFile("/src/music/b.flac", 2000)
                .WithDirectory("/dest")
                .WithSimulatedFile("/dest/src/music/a.flac", 1000)); // a.flac already exists at dest

        var root = await provider.BuildDirectoryTree();
        var dirNode = root.FindNodeByPathSegments(["src", "music"]);
        Assert.NotNull(dirNode);
        dirNode.FilterResult = FilterResult.Included;
        dirNode.CheckState = CheckState.Checked;

        var ct = CancellationToken.None;
        var runner = new PipelineRunner(new TransformPipeline([new CopyStep("/mem/dest")]));
        var plan = await runner.PreviewAsync(
            new PipelineJob
            {
                RootNode       = root,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode  = OverwriteMode.Always,
            }, ct);

        // One action per file, not one per directory
        Assert.Equal(2, plan.Actions.Count);
        Assert.Equal(2, plan.TotalFilesAffected);

        // a.flac already exists → Overwritten; b.flac is new → Created
        var aAction = plan.Actions.Single(a => a.SourcePath.EndsWith("a.flac"));
        var bAction = plan.Actions.Single(a => a.SourcePath.EndsWith("b.flac"));
        Assert.Equal(DestinationResult.Overwritten, aAction.DestinationResult);
        Assert.Equal(DestinationResult.Created, bAction.DestinationResult);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Move: topmost selected dir is moved atomically (not individual files)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CollectSelectedNodes_ReturnsDirNode_NotChildren()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithSimulatedFile("/src/music/a.flac", 1000)
            .WithSimulatedFile("/src/music/b.flac", 2000)
            .WithDirectory("/dest"));

        var root = await provider.BuildDirectoryTree();
        var dirNode = root.FindNodeByPathSegments(["src", "music"]);
        Assert.NotNull(dirNode);
        dirNode.FilterResult = FilterResult.Included;
        dirNode.CheckState = CheckState.Checked;

        // With dirNode (src/music) selected, the pipeline should see 1 atomic move result,
        // not 2 individual file results.
        var ct = CancellationToken.None;
        var runner = new PipelineRunner(new TransformPipeline([new MoveStep("/mem/dest")]));
        var results = await runner.ExecuteAsync(
            new PipelineJob
            {
                RootNode       = root,
                SourceProvider = provider,
                ProviderRegistry = provider.CreateRegistry(),
                OverwriteMode  = OverwriteMode.Always,
            });

        Assert.Single(results);
        Assert.Equal(SourceResult.Moved, results[0].SourceNodeResult);
    }
}
