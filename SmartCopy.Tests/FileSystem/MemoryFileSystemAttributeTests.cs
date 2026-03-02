using SmartCopy.Core.FileSystem;
using SmartCopy.Tests.TestInfrastructure;

namespace SmartCopy.Tests.FileSystem;

/// <summary>
/// Verifies that <see cref="MemoryFileSystemProvider"/> stores and returns
/// <see cref="FileAttributes"/> exactly as seeded.
/// </summary>
public sealed class MemoryFileSystemAttributeTests
{
    [Fact]
    public async Task HiddenFile_HasHiddenAttribute()
    {
        var provider = MemoryFileSystemFixtures.Create(f =>
            f.WithFile("/hidden.txt", "x"u8, FileAttributes.Hidden));

        var node = await provider.GetNodeAsync("/hidden.txt", CancellationToken.None);

        Assert.Equal(FileAttributes.Hidden, node.Attributes);
    }

    [Fact]
    public async Task ReadOnlyFile_HasReadOnlyAttribute()
    {
        var provider = MemoryFileSystemFixtures.Create(f =>
            f.WithFile("/readonly.txt", "x"u8, FileAttributes.ReadOnly));

        var node = await provider.GetNodeAsync("/readonly.txt", CancellationToken.None);

        Assert.Equal(FileAttributes.ReadOnly, node.Attributes);
    }

    [Fact]
    public async Task HiddenDirectory_HasCombinedAttributes()
    {
        var attrs = FileAttributes.Directory | FileAttributes.Hidden;
        var provider = MemoryFileSystemFixtures.Create(f =>
            f.WithDirectory("/hidden-dir", attrs));

        var node = await provider.GetNodeAsync("/hidden-dir", CancellationToken.None);

        Assert.Equal(attrs, node.Attributes);
    }

    [Fact]
    public async Task NormalFile_DefaultsToNormalAttribute()
    {
        var provider = MemoryFileSystemFixtures.Create(f =>
            f.WithFile("/normal.txt", "x"u8));

        var node = await provider.GetNodeAsync("/normal.txt", CancellationToken.None);

        Assert.Equal(FileAttributes.Normal, node.Attributes);
    }
}
