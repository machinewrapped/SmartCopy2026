using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Pipeline;

/// <summary>
/// Implemented by pipeline steps that write data to a target volume,
/// allowing <see cref="PipelineRunner"/> to perform a pre-flight free-space check.
/// </summary>
public interface IHasFreeSpaceCheck
{
    /// <summary>
    /// Returns the provider that will receive output from this step,
    /// or <see langword="null"/> if no free-space check is needed
    /// (e.g., same-volume moves that require no additional space).
    /// </summary>
    IFileSystemProvider? ResolveFreeSpaceTarget(IFileSystemProvider sourceProvider, FileSystemProviderRegistry registry);
}
