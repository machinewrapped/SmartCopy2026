using System.Collections.Generic;

namespace SmartCopy.Core.Filters;

public sealed record FilterChainConfig(
    string Name,
    string? Description,
    List<FilterConfig> Filters);

