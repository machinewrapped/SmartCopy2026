using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline.Validation;

public sealed record PipelineValidationContext(
    IFileSystemProvider? SourceProvider,
    IPathResolver ProviderRegistry,
    FreeSpaceCache CachedFreeSpace,
    long SelectedBytes = 0,
    int SelectedFileCount = 0,
    int NumFilterIncludedFiles = 0,
    long TotalFilterIncludedBytes = 0);
