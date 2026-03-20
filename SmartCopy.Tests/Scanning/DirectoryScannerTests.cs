using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Scanning;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.Scanning;

public sealed class DirectoryScannerTests
{
    [Fact]
    public async Task ScanAsync_StreamsTopLevelThenChildren()
    {
        var provider = MemoryFileSystemFixtures.Create(fixture => fixture
            .WithDirectory("/root")
            .WithDirectory("/root/rock")
            .WithDirectory("/root/rock/beatles")
            .WithFile("/root/rock/beatles/song.mp3", "x"u8)
            .WithDirectory("/root/jazz"));

        var scanner = new DirectoryScanner(provider);
        var results = new List<DirectoryTreeNode>();
        await foreach (var node in scanner.ScanAsync(
                           "/root",
                           new ScanOptions { LazyExpand = false, IncludeHidden = true },
                           progress: null,
                           ct: CancellationToken.None))
        {
            results.Add(node);
        }

        Assert.NotEmpty(results);
        // Root is always the first yielded node (CanonicalRelativePath == "")
        var rockIndex = results.FindIndex(n => n.CanonicalRelativePath == "rock");
        var jazzIndex = results.FindIndex(n => n.CanonicalRelativePath == "jazz");
        var deepChildIndex = results.FindIndex(n => n.CanonicalRelativePath == "rock/beatles/song.mp3");

        Assert.True(rockIndex >= 0, "Expected top-level 'rock' folder to be returned.");
        Assert.True(jazzIndex >= 0, "Expected top-level 'jazz' folder to be returned.");
        Assert.True(deepChildIndex >= 0, "Expected deep child song file to be returned.");

        // Progressive scan contract: top-level children are emitted before descendants.
        Assert.True(rockIndex < deepChildIndex, "Expected top-level nodes before deep descendants.");
        Assert.True(jazzIndex < deepChildIndex, "Expected top-level nodes before deep descendants.");

        // File/directory separation: files land in Files, not Children.
        var song = results.Single(n => n.Name == "song.mp3") as FileNode;
        Assert.NotNull(song);
        Assert.Contains(song, song.Parent!.Files);
        Assert.DoesNotContain(song, song.Parent!.Children.Cast<DirectoryTreeNode>());
    }

    [Fact]
    public async Task ScanAsync_RespectsMaxDepth()
    {
        var provider = MemoryFileSystemFixtures.Create(fixture => fixture
            .WithDirectory("/root")
            .WithDirectory("/root/a")
            .WithDirectory("/root/a/b")
            .WithFile("/root/a/b/file.txt", "x"u8));

        var scanner = new DirectoryScanner(provider);
        var results = new List<DirectoryTreeNode>();
        await foreach (var node in scanner.ScanAsync(
                           "/root",
                           new ScanOptions { LazyExpand = false, MaxDepth = 1, IncludeHidden = true },
                           progress: null,
                           ct: CancellationToken.None))
        {
            results.Add(node);
        }

        Assert.DoesNotContain(results, n => n.Name == "file.txt");
    }
}
