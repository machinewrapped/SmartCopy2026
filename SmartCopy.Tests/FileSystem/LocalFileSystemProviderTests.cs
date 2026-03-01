using System.IO;
using System.Linq;
using System.Text;
using SmartCopy.Core.FileSystem;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.FileSystem;

public sealed class LocalFileSystemProviderTests
{
    [Fact]
    public async Task GetChildrenAndGetNode_ReturnExpectedMetadata()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "albums"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "albums", "track1.txt"), "abc");

        var provider = new LocalFileSystemProvider(temp.Path);
        var children = await provider.GetChildrenAsync(Path.Combine(temp.Path, "albums"), CancellationToken.None);

        Assert.Single(children);
        Assert.Equal("track1.txt", children.Single().Name);

        var node = await provider.GetNodeAsync(Path.Combine(temp.Path, "albums", "track1.txt"), CancellationToken.None);
        var relativePath = provider.GetRelativePath(provider.RootPath, node.FullPath);
        var pathSegments = provider.SplitPath(relativePath);
        Assert.Equal(3, node.Size);
        Assert.Equal(Path.Combine("albums", "track1.txt"), string.Join(Path.DirectorySeparatorChar, pathSegments));
    }

    [Fact]
    public async Task WriteMoveDelete_WorkEndToEnd()
    {
        using var temp = new TempDirectory();
        var provider = new LocalFileSystemProvider(temp.Path);

        await provider.CreateDirectoryAsync("inbox", CancellationToken.None);

        await using var payload = new MemoryStream(Encoding.UTF8.GetBytes("payload"));
        await provider.WriteAsync("inbox/file.txt", payload, progress: null, CancellationToken.None);
        Assert.True(await provider.ExistsAsync("inbox/file.txt", CancellationToken.None));

        await provider.MoveAsync("inbox/file.txt", "archive/file.txt", CancellationToken.None);
        Assert.True(await provider.ExistsAsync("archive/file.txt", CancellationToken.None));
        Assert.False(await provider.ExistsAsync("inbox/file.txt", CancellationToken.None));

        await provider.DeleteAsync("archive/file.txt", CancellationToken.None);
        Assert.False(await provider.ExistsAsync("archive/file.txt", CancellationToken.None));
    }
}

