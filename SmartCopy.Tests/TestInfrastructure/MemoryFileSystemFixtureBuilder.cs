using System;
using System.Text;
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

    public MemoryFileSystemProvider Build() => _provider;
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
}
