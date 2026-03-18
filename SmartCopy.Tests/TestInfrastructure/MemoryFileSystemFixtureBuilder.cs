using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Tests.TestInfrastructure;

public sealed class MemoryFileSystemFixtureBuilder(string? customRootPath = null, string? volumeId = null)
{
    private readonly MemoryFileSystemProvider _provider = new(customRootPath: customRootPath, volumeId: volumeId);

    public MemoryFileSystemFixtureBuilder WithDirectory(string path,
        FileAttributes attributes = FileAttributes.Directory)
    {
        _provider.SeedDirectory(path, attributes);
        return this;
    }

    public MemoryFileSystemFixtureBuilder WithFile(string path, ReadOnlySpan<byte> content,
        FileAttributes attributes = FileAttributes.Normal)
    {
        _provider.SeedFile(path, content, attributes);
        return this;
    }

    public MemoryFileSystemFixtureBuilder WithTextFile(string path, string content,
        FileAttributes attributes = FileAttributes.Normal)
    {
        _provider.SeedFile(path, System.Text.Encoding.UTF8.GetBytes(content), attributes);
        return this;
    }

    public MemoryFileSystemFixtureBuilder WithSimulatedFile(string path, long size,
        FileAttributes attributes = FileAttributes.Normal)
    {
        _provider.SeedSimulatedFile(path, size, attributes);
        return this;
    }

    public MemoryFileSystemProvider Build()
    {
        return _provider;
    }
}

public static class MemoryFileSystemFixtures
{
    public static MemoryFileSystemProvider Create(Action<MemoryFileSystemFixtureBuilder> configure, string? customRootPath = null, string? volumeId = null)
    {
        var builder = new MemoryFileSystemFixtureBuilder(customRootPath, volumeId);
        configure(builder);
        return builder.Build();
    }

    internal static async Task<DirectoryNode> BuildDirectoryTree(Action<MemoryFileSystemFixtureBuilder> configure, string? customRootPath = null)
    {
        var builder = new MemoryFileSystemFixtureBuilder(customRootPath);
        configure(builder);
        return await builder.Build().BuildDirectoryTree();
    }
}
