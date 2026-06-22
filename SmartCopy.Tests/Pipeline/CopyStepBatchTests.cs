using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Pipeline;

/// <summary>
/// Integration tests for the batch copy path in CopyStep.
/// Uses MemoryFileSystemProvider — no physical disk I/O.
/// </summary>
public sealed class CopyStepBatchTests
{
    [Fact]
    public async Task BatchCopy_ProducesBitIdenticalFiles()
    {
        var content1 = "hello from file one"u8.ToArray();
        var content2 = "hello from file two"u8.ToArray();
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithFile("/src/a.txt", content1)
            .WithFile("/src/b.txt", content2)
            .WithDirectory("/dest"));

        var root = await provider.BuildDirectoryTree("/src");
        SelectAllFiles(root);

        await RunCopyAsync(root, provider, batchBufferBytes: 512 * 1024);

        Assert.Equal(content1, await ReadAllBytesAsync(provider, "/dest/a.txt"));
        Assert.Equal(content2, await ReadAllBytesAsync(provider, "/dest/b.txt"));
    }

    [Fact]
    public async Task BatchCopy_CopiedCountMatchesFileCount()
    {
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithFile("/src/a.txt", "AAA"u8)
            .WithFile("/src/b.txt", "BBB"u8)
            .WithFile("/src/c.txt", "CCC"u8)
            .WithDirectory("/dest"));

        var root = await provider.BuildDirectoryTree("/src");
        SelectAllFiles(root);

        var results = await RunCopyAsync(root, provider, batchBufferBytes: 512 * 1024);

        Assert.Equal(3, results.Count(r => r.SourceNodeResult == SourceResult.Copied && r.IsSuccess));
    }

    [Fact]
    public async Task BatchCopy_FileLargerThanBuffer_UsesFallback()
    {
        // Buffer = 10 bytes; the large file routes through the unbatched fallback.
        var small = "tiny"u8.ToArray();
        var large = new byte[20];
        Random.Shared.NextBytes(large);

        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithFile("/src/small.txt", small)
            .WithFile("/src/large.txt", large)
            .WithDirectory("/dest"));

        var root = await provider.BuildDirectoryTree("/src");
        SelectAllFiles(root);

        var results = await RunCopyAsync(root, provider, batchBufferBytes: 10);

        Assert.Equal(2, results.Count(r => r.SourceNodeResult == SourceResult.Copied && r.IsSuccess));
        Assert.Equal(large, await ReadAllBytesAsync(provider, "/dest/large.txt"));
    }

    [Fact]
    public async Task BatchCopy_FlushOnOverflow_AllFilesCorrect()
    {
        // Buffer fits exactly 2 × 4-byte files; the third forces a mid-iteration flush.
        var a = "AAAA"u8.ToArray();
        var b = "BBBB"u8.ToArray();
        var c = "CCCC"u8.ToArray();

        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithFile("/src/a.txt", a)
            .WithFile("/src/b.txt", b)
            .WithFile("/src/c.txt", c)
            .WithDirectory("/dest"));

        var root = await provider.BuildDirectoryTree("/src");
        SelectAllFiles(root);

        var results = await RunCopyAsync(root, provider, batchBufferBytes: 8);

        Assert.Equal(3, results.Count(r => r.SourceNodeResult == SourceResult.Copied));
        Assert.Equal(a, await ReadAllBytesAsync(provider, "/dest/a.txt"));
        Assert.Equal(b, await ReadAllBytesAsync(provider, "/dest/b.txt"));
        Assert.Equal(c, await ReadAllBytesAsync(provider, "/dest/c.txt"));
    }

    [Fact]
    public async Task BatchDisabled_UnbatchedPathStillWorks()
    {
        var content = "hello world"u8.ToArray();
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithFile("/src/file.txt", content)
            .WithDirectory("/dest"));

        var root = await provider.BuildDirectoryTree("/src");
        SelectAllFiles(root);

        // batchBufferBytes = 0 → batch disabled
        var results = await RunCopyAsync(root, provider, batchBufferBytes: 0);

        Assert.Contains(results, r => r.SourceNodeResult == SourceResult.Copied && r.IsSuccess);
        Assert.Equal(content, await ReadAllBytesAsync(provider, "/dest/file.txt"));
    }

    [Fact]
    public async Task BatchCopy_EmitsDepthFirst_FilesSizeSortedWithinDirectory()
    {
        // Two sibling subtrees, files stored alphabetically (x,y,z / p,q) — deliberately *not* by size.
        // The batch path must complete each subtree in turn (depth-first) with its files ascending by
        // size. This is the sole guard for that ordering contract — other tests check counts/content.
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithFile("/src/A/x.txt", new byte[3])
            .WithFile("/src/A/y.txt", new byte[1])
            .WithFile("/src/A/z.txt", new byte[2])
            .WithFile("/src/B/p.txt", new byte[2])
            .WithFile("/src/B/q.txt", new byte[1])
            .WithDirectory("/dest"));

        var root = await provider.BuildDirectoryTree("/src");
        SelectAllFiles(root);

        var results = await RunCopyAsync(root, provider, batchBufferBytes: 512 * 1024);

        var copiedOrder = results
            .Where(r => r.SourceNodeResult == SourceResult.Copied && r.IsSuccess)
            .Select(r => r.SourceNode.Name)
            .ToList();

        Assert.Equal(new[] { "y.txt", "z.txt", "x.txt", "q.txt", "p.txt" }, copiedOrder);
    }

    [Fact]
    public async Task BatchCopy_PartiallySelectedDirectory_StillCopiesSelectedFiles()
    {
        // The "music" subdirectory holds one selected file (keep.txt) and one that is both unchecked and
        // filter-Excluded (drop.txt), making the directory itself Indeterminate *and* Mixed — neither
        // IsSelected nor a prunable subtree. The batch traversal must still recurse in and copy keep.txt.
        // This pins both prune polarities: pruning on != Checked (Indeterminate) or != Included (Mixed),
        // rather than == Unchecked / == Excluded, would silently drop keep.txt (data loss).
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithFile("/src/music/keep.txt", "KEEP"u8)
            .WithFile("/src/music/drop.txt", "DROP"u8)
            .WithDirectory("/dest"));

        var root = await provider.BuildDirectoryTree("/src");

        var keep = root.FindNodeByPathSegments("music", "keep.txt");
        var drop = root.FindNodeByPathSegments("music", "drop.txt");
        Assert.NotNull(keep);
        Assert.NotNull(drop);
        keep.FilterResult = FilterResult.Included;
        keep.CheckState = CheckState.Checked;
        drop.FilterResult = FilterResult.Excluded; // → "music" is Mixed, not Excluded
        // (drop.txt stays unchecked → "music" is Indeterminate, not Unchecked.)

        keep.Parent!.FilterResult = FilterResult.Mixed;

        var results = await RunCopyAsync(root, provider, batchBufferBytes: 512 * 1024);

        Assert.Equal(1, results.Count(r => r.SourceNodeResult == SourceResult.Copied && r.IsSuccess));
        Assert.Equal("KEEP"u8.ToArray(), await ReadAllBytesAsync(provider, "/dest/music/keep.txt"));
        Assert.False(await provider.ExistsAsync("/dest/music/drop.txt", CancellationToken.None));
    }

    [Fact]
    public async Task BatchCopy_AttributesExecutionDurationEquallyPerFile()
    {
        // The batch's read + flush time is split evenly across its files, so files of *different* sizes
        // in one flush still get equal ExecutionDuration — small-file copy time is overhead-dominated,
        // not byte-proportional. This pins the even split (a bytes-proportional split would give 1:4:16)
        // and guards against the old attribution that dumped the batch's read cost on the first file.
        var provider = MemoryFileSystemFixtures.Create(f => f
            .WithFile("/src/a.bin", new byte[1024])
            .WithFile("/src/b.bin", new byte[4096])
            .WithFile("/src/c.bin", new byte[16384])
            .WithDirectory("/dest"));

        var root = await provider.BuildDirectoryTree("/src");
        SelectAllFiles(root);

        var results = await RunCopyAsync(root, provider, batchBufferBytes: 512 * 1024);

        var durations = results
            .Where(r => r.SourceNodeResult == SourceResult.Copied && r.IsSuccess)
            .Select(r => r.ExecutionDuration)
            .ToList();

        Assert.Equal(3, durations.Count);
        Assert.All(durations, d => Assert.NotNull(d));
        // Different sizes, one flush → identical share (each is batchElapsed / 3).
        Assert.Equal(durations[0], durations[1]);
        Assert.Equal(durations[1], durations[2]);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void SelectAllFiles(DirectoryNode dir)
    {
        // Select the directory itself too, so it is emitted as a traversal marker. (Recursion into
        // children is unconditional, but a directory is only yielded when its own CheckState is set.)
        dir.FilterResult = FilterResult.Included;
        dir.CheckState = CheckState.Checked;
        foreach (var file in dir.Files)
        {
            file.FilterResult = FilterResult.Included;
            file.CheckState = CheckState.Checked;
        }
        foreach (var child in dir.Children)
            SelectAllFiles(child);
    }

    private static async Task<IReadOnlyList<TransformResult>> RunCopyAsync(
        DirectoryNode root,
        MemoryFileSystemProvider provider,
        long batchBufferBytes = 0)
    {
        var step = new CopyStep("mem://dest");
        var runner = new PipelineRunner(new TransformPipeline([step]));
        var job = new PipelineJob
        {
            RootNode = root,
            SourceProvider = provider,
            ProviderRegistry = provider.CreateRegistry(),
            OperationalSettings = new OperationalSettings { BatchBufferBytes = batchBufferBytes },
        };
        return await runner.ExecuteAsync(job);
    }

    private static async Task<byte[]> ReadAllBytesAsync(MemoryFileSystemProvider provider, string path)
    {
        await using var stream = await provider.OpenReadAsync(path, ct: CancellationToken.None);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
    }
}
