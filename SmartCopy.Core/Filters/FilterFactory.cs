using System;
using SmartCopy.Core.Filters.Filters;

namespace SmartCopy.Core.Filters;

/// <summary>
/// Reconstructs a concrete <see cref="IFilter"/> from a serialised <see cref="FilterConfig"/>.
/// Used by <see cref="FilterChain.FromConfig"/> and <see cref="FilterPresetStore"/>.
/// </summary>
public static class FilterFactory
{
    public static IFilter FromConfig(FilterConfig config)
    {
        var mode = config.Mode == "Include"
            ? FilterMode.Only  // backward compat: old "Include" → Only
            : Enum.TryParse<FilterMode>(config.Mode, out var m) ? m : FilterMode.Only;
        var filter = config.FilterType switch
        {
            "Extension"  => BuildExtension(config, mode),
            "Wildcard"   => BuildWildcard(config, mode),
            "DateRange"  => BuildDateRange(config, mode),
            "SizeRange"  => BuildSizeRange(config, mode),
            "Mirror"     => BuildMirror(config, mode),
            "Attribute"  => BuildAttribute(config, mode),
            _ => throw new InvalidOperationException($"Unknown filter type: {config.FilterType}")
        };

        filter.CustomName = config.CustomName;
        return filter;
    }

    private static IFilter BuildExtension(FilterConfig c, FilterMode mode)
    {
        var raw = c.Parameters["extensions"]?.GetValue<string>() ?? string.Empty;
        var extensions = ExtensionFilter.ParseExtensions(raw);
        return new ExtensionFilter(extensions, mode, c.IsEnabled);
    }

    private static IFilter BuildWildcard(FilterConfig c, FilterMode mode)
    {
        var pattern = c.Parameters["pattern"]?.GetValue<string>() ?? string.Empty;
        return new WildcardFilter(pattern, mode, c.IsEnabled);
    }

    private static IFilter BuildDateRange(FilterConfig c, FilterMode mode)
    {
        var fieldStr = c.Parameters["field"]?.GetValue<string>() ?? "Modified";
        var field = Enum.Parse<DateField>(fieldStr);

        DateTime? min = null;
        DateTime? max = null;

        if (c.Parameters.TryGetPropertyValue("min", out var minNode) && minNode is not null)
        {
            min = minNode.GetValue<DateTime>();
        }

        if (c.Parameters.TryGetPropertyValue("max", out var maxNode) && maxNode is not null)
        {
            max = maxNode.GetValue<DateTime>();
        }

        return new DateRangeFilter(field, min, max, mode, c.IsEnabled);
    }

    private static IFilter BuildSizeRange(FilterConfig c, FilterMode mode)
    {
        long? minBytes = null;
        long? maxBytes = null;

        if (c.Parameters.TryGetPropertyValue("minBytes", out var minNode) && minNode is not null)
        {
            minBytes = minNode.GetValue<long>();
        }

        if (c.Parameters.TryGetPropertyValue("maxBytes", out var maxNode) && maxNode is not null)
        {
            maxBytes = maxNode.GetValue<long>();
        }

        return new SizeRangeFilter(minBytes, maxBytes, mode, c.IsEnabled);
    }

    private static IFilter BuildMirror(FilterConfig c, FilterMode mode)
    {
        var comparisonPath = c.Parameters["comparisonPath"]?.GetValue<string>() ?? string.Empty;
        var compareModeStr = c.Parameters["compareMode"]?.GetValue<string>() ?? "NameAndSize";
        var compareMode = Enum.Parse<MirrorCompareMode>(compareModeStr);
        var useAutomaticPath = c.Parameters.TryGetPropertyValue("useAutomaticPath", out var autoNode)
            && autoNode is not null
            && autoNode.GetValue<bool>();
        return new MirrorFilter(comparisonPath, compareMode, mode, c.IsEnabled, useAutomaticPath);
    }

    private static IFilter BuildAttribute(FilterConfig c, FilterMode mode)
    {
        var attributesStr = c.Parameters["attributes"]?.GetValue<string>() ?? string.Empty;
        var attributes = Enum.Parse<System.IO.FileAttributes>(attributesStr);
        return new AttributeFilter(attributes, mode, c.IsEnabled);
    }
}
