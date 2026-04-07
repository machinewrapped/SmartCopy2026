namespace SmartCopy.Core.Filters;

/// <summary>
/// Implemented by filters that can use the pipeline's destination path
/// as context for their evaluation (e.g. mirror against the copy destination).
/// </summary>
public interface IPipelineAwareFilter
{
    /// <summary>
    /// The current pipeline destination path, injected before filter evaluation
    /// </summary>
    string? PipelineDestinationPath { get; set; }
}
