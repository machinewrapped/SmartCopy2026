using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Scanning;

namespace SmartCopy.Tests.Scanning;

public sealed class DirectoryScannerTests
{
    [Fact]
    public async Task ScanAsync_StreamsTopLevelThenChildren()
    {
        var provider = new MemoryFileSystemProvider();
        provider.SeedDirectory("/root");
        provider.SeedDirectory("/root/rock");
        provider.SeedDirectory("/root/rock/beatles");
        provider.SeedFile("/root/rock/beatles/song.mp3", "x"u8);
        provider.SeedDirectory("/root/jazz");

        var scanner = new DirectoryScanner();
        var results = new List<FileSystemNode>();
        await foreach (var node in scanner.ScanAsync(
                           provider,
                           "/root",
                           new ScanOptions { LazyExpand = false, IncludeHidden = true },
                           progress: null,
                           ct: CancellationToken.None))
        {
            results.Add(node);
        }

        Assert.NotEmpty(results);
        var rockIndex = results.FindIndex(n => n.RelativePath == "root/rock");
        var jazzIndex = results.FindIndex(n => n.RelativePath == "root/jazz");
        var deepChildIndex = results.FindIndex(n => n.RelativePath == "root/rock/beatles/song.mp3");

        Assert.True(rockIndex >= 0, "Expected top-level 'rock' folder to be returned.");
        Assert.True(jazzIndex >= 0, "Expected top-level 'jazz' folder to be returned.");
        Assert.True(deepChildIndex >= 0, "Expected deep child song file to be returned.");

        // Progressive scan contract: top-level children are emitted before descendants.
        Assert.True(rockIndex < deepChildIndex, "Expected top-level nodes before deep descendants.");
        Assert.True(jazzIndex < deepChildIndex, "Expected top-level nodes before deep descendants.");
    }

    [Fact]
    public async Task ScanAsync_RespectsMaxDepth()
    {
        var provider = new MemoryFileSystemProvider();
        provider.SeedDirectory("/root");
        provider.SeedDirectory("/root/a");
        provider.SeedDirectory("/root/a/b");
        provider.SeedFile("/root/a/b/file.txt", "x"u8);

        var scanner = new DirectoryScanner();
        var results = new List<FileSystemNode>();
        await foreach (var node in scanner.ScanAsync(
                           provider,
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
