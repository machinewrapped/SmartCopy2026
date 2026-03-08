using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline.Validation;

public sealed record PipelineValidationContext(
    IFileSystemProvider? SourceProvider,
    IPathResolver ProviderRegistry,
    IReadOnlyDictionary<string, long?> CachedFreeSpace,
    bool HasSelectedIncludedInputs = true,
    long SelectedBytes = 0);
