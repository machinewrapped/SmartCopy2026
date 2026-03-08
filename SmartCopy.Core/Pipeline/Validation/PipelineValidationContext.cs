using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline.Validation;

public sealed record PipelineValidationContext(
    bool HasSelectedIncludedInputs = true,
    long SelectedBytes = 0,
    IFileSystemProvider? SourceProvider = null,
    IPathResolver? ProviderRegistry = null);
