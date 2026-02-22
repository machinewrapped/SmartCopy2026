using System.Text.Json.Nodes;

namespace SmartCopy.Core.Filters;

public sealed record FilterConfig(
    string FilterType,
    bool IsEnabled,
    string Mode,
    JsonObject Parameters,
    string? CustomName = null);

