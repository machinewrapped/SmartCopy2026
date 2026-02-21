using System.IO;
using System.Linq;
using System.Text;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Tests.FileSystem;

public sealed class MemoryFileSystemProviderTests
{
    [Fact]
    public async Task WriteReadAndEnumerate_WorksEndToEnd()
    {
        var provider = new MemoryFileSystemProvider();
        provider.SeedDirectory("/music");

        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello world"));
        await provider.WriteAsync("/music/track.txt", content, progress: null, CancellationToken.None);

        var exists = await provider.ExistsAsync("/music/track.txt", CancellationToken.None);
        Assert.True(exists);

        var node = await provider.GetNodeAsync("/music/track.txt", CancellationToken.None);
        Assert.False(node.IsDirectory);
        Assert.Equal("track.txt", node.Name);
        Assert.Equal("music/track.txt", node.RelativePath);

        var children = await provider.GetChildrenAsync("/music", CancellationToken.None);
        Assert.Single(children);
        Assert.Equal("track.txt", children[0].Name);

        await using var read = await provider.OpenReadAsync("/music/track.txt", CancellationToken.None);
        using var reader = new StreamReader(read);
        var text = await reader.ReadToEndAsync();
        Assert.Equal("hello world", text);
    }

    [Fact]
    public async Task MoveAndDelete_WorkForFilesAndDirectories()
    {
        var provider = new MemoryFileSystemProvider();
        provider.SeedDirectory("/source");
        provider.SeedFile("/source/a.txt", "A"u8);
        provider.SeedDirectory("/source/sub");
        provider.SeedFile("/source/sub/b.txt", "B"u8);

        await provider.MoveAsync("/source", "/dest", CancellationToken.None);

        Assert.True(await provider.ExistsAsync("/dest/a.txt", CancellationToken.None));
        Assert.True(await provider.ExistsAsync("/dest/sub/b.txt", CancellationToken.None));
        Assert.False(await provider.ExistsAsync("/source", CancellationToken.None));

        await provider.DeleteAsync("/dest/sub", CancellationToken.None);
        Assert.False(await provider.ExistsAsync("/dest/sub", CancellationToken.None));

        var children = await provider.GetChildrenAsync("/dest", CancellationToken.None);
        Assert.Single(children);
        Assert.Equal("a.txt", children.Single().Name);
    }
}

