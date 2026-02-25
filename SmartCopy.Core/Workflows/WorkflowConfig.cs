using SmartCopy.Core.Filters;
using SmartCopy.Core.Pipeline;

namespace SmartCopy.Core.Workflows;

public sealed record WorkflowConfig(
    string Name,
    string? Description,
    string SourcePath,
    FilterChainConfig FilterChain,
    PipelineConfig Pipeline);
