using System;
using System.Collections.Generic;
using System.Linq;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Filters;

public sealed class FilterChain
{
    private readonly List<IFilter> _filters = [];

    public FilterChain(IEnumerable<IFilter> filters)
    {
        _filters.AddRange(filters);
    }

    public IReadOnlyList<IFilter> Filters => _filters;

    public IEnumerable<FileSystemNode> Apply(
        IEnumerable<FileSystemNode> nodes,
        IFileSystemProvider? comparisonProvider = null)
    {
        foreach (var node in nodes)
        {
            if (EvaluateNode(node, comparisonProvider, out _, out _))
            {
                yield return node;
            }
        }
    }

    public void ApplyToTree(
        IEnumerable<FileSystemNode> roots,
        IFileSystemProvider? comparisonProvider = null)
    {
        foreach (var root in roots)
        {
            ApplyToTreeNode(root, comparisonProvider);
        }
    }

    public FilterChainConfig ToConfig(string name = "Default", string? description = null)
    {
        return new FilterChainConfig(
            Name: name,
            Description: description,
            Filters: _filters.Select(filter => filter.Config).ToList());
    }

    public static FilterChain FromConfig(FilterChainConfig config, Func<FilterConfig, IFilter> filterFactory)
    {
        var filters = config.Filters.Select(filterFactory);
        return new FilterChain(filters);
    }

    private void ApplyToTreeNode(FileSystemNode node, IFileSystemProvider? comparisonProvider)
    {
        var included = EvaluateNode(node, comparisonProvider, out var excludedByFilter, out _);
        node.FilterResult = included ? FilterResult.Included : FilterResult.Excluded;
        node.ExcludedByFilter = included ? null : excludedByFilter;

        foreach (var child in node.Children)
        {
            ApplyToTreeNode(child, comparisonProvider);
        }
    }

    private bool EvaluateNode(
        FileSystemNode node,
        IFileSystemProvider? comparisonProvider,
        out string? excludedByFilter,
        out IFilter? matchingFilter)
    {
        excludedByFilter = null;
        matchingFilter = null;

        foreach (var filter in _filters.Where(f => f.IsEnabled))
        {
            var matches = filter.Matches(node, comparisonProvider);
            if (filter.Mode == FilterMode.Include && !matches)
            {
                excludedByFilter = filter.Name;
                matchingFilter = filter;
                return false;
            }

            if (filter.Mode == FilterMode.Exclude && matches)
            {
                excludedByFilter = filter.Name;
                matchingFilter = filter;
                return false;
            }
        }

        return true;
    }
}

