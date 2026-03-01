using System;
using System.Text;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Tests.TestInfrastructure;

public sealed class MemoryFileSystemFixtureBuilder
{
    private readonly MemoryFileSystemProvider _provider = new();

    public MemoryFileSystemFixtureBuilder WithDirectory(string path)
    {
        _provider.SeedDirectory(path);
        return this;
    }

    public MemoryFileSystemFixtureBuilder WithFile(string path, ReadOnlySpan<byte> content)
    {
        _provider.SeedFile(path, content);
        return this;
    }

    public MemoryFileSystemFixtureBuilder WithTextFile(string path, string content)
    {
        _provider.SeedFile(path, Encoding.UTF8.GetBytes(content));
        return this;
    }

    public MemoryFileSystemFixtureBuilder WithSimulatedFile(string path, long size)
    {
        _provider.SeedSimulatedFile(path, size);
        return this;
    }

    public MemoryFileSystemProvider Build()
    {
        return _provider;
    }
}

public static class MemoryFileSystemFixtures
{
    public static MemoryFileSystemProvider Create(Action<MemoryFileSystemFixtureBuilder> configure)
    {
        var builder = new MemoryFileSystemFixtureBuilder();
        configure(builder);
        return builder.Build();
    }

    public static (MemoryFileSystemProvider Source, MemoryFileSystemProvider Target) CreatePair(
        Action<MemoryFileSystemFixtureBuilder> configureSource,
        Action<MemoryFileSystemFixtureBuilder>? configureTarget = null)
    {
        var sourceBuilder = new MemoryFileSystemFixtureBuilder();
        configureSource(sourceBuilder);

        var targetBuilder = new MemoryFileSystemFixtureBuilder();
        configureTarget?.Invoke(targetBuilder);

        return (sourceBuilder.Build(), targetBuilder.Build());
    }

    internal static async Task<DirectoryTreeNode> BuildDirectoryTree(Action<MemoryFileSystemFixtureBuilder> configure)
    {
        var builder = new MemoryFileSystemFixtureBuilder();
        configure(builder);
        MemoryFileSystemProvider provider = builder.Build();
        return await BuildDirectoryTree(provider);
    }

    internal static async Task<DirectoryTreeNode> BuildDirectoryTree(MemoryFileSystemProvider provider, string? path = null)
    {
        path ??= provider.RootPath;

        FileSystemNode rootNode = await provider.GetNodeAsync(path, CancellationToken.None);
        if (rootNode is null)
        {
            throw new InvalidOperationException($"Root path '{path}' does not exist in the file system fixture.");
        }

        return await BuildDirectoryTreeNode(provider, rootNode, parent: null);
    }

    private static async Task<DirectoryTreeNode> BuildDirectoryTreeNode(MemoryFileSystemProvider provider, FileSystemNode entry, DirectoryTreeNode? parent)
    {
        var node = new DirectoryTreeNode(entry, parent);

        if (!entry.IsDirectory)
            return node;

        var children = await provider.GetChildrenAsync(entry.FullPath, CancellationToken.None);
        foreach (var child in children)
        {
            var childNode = await BuildDirectoryTreeNode(provider, child, parent: node);
            if (child.IsDirectory)
            {
                node.Children.Add(childNode);
            }
            else
            {
                node.Files.Add(childNode);
            }
        }

        return node;
    }
}
