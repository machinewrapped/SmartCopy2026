using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Filters;

public sealed class FilterContext : IFilterContext
{
    private readonly FileSystemProviderRegistry _registry;

    /// <summary>Wraps an empty registry — resolves local paths only.</summary>
    public FilterContext() : this(FileSystemProviderRegistry.Empty) { }

    /// <summary>Wraps an existing registry (all registered providers visible).</summary>
    public FilterContext(FileSystemProviderRegistry registry) => _registry = registry;

    /// <summary>Creates a context with an explicit set of prefix→provider mappings.</summary>
    public FilterContext(IFileSystemProvider provider)
    {
        _registry = new FileSystemProviderRegistry();
        _registry.Register(provider);
    }

    /// <summary>Convenience: register a single provider under its own RootPath.</summary>
    public static FilterContext FromProvider(IFileSystemProvider provider) => new(provider);

    /// <summary>Convenience: local paths only, no explicit registrations.</summary>
    public static FilterContext LocalOnly { get; } = new();

    public IFileSystemProvider? ResolveProvider(string path) => _registry.Resolve(path);
}
