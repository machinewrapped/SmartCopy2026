using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    public async Task<IReadOnlyList<FileSystemNode>> ApplyAsync(
        IEnumerable<FileSystemNode> nodes,
        IFileSystemProvider? comparisonProvider = null,
        CancellationToken ct = default)
    {
        var result = new List<FileSystemNode>();
        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();
            var evaluation = await EvaluateNodeAsync(node, comparisonProvider, ct);
            if (evaluation.IsIncluded)
            {
                result.Add(node);
            }
        }

        return result;
    }

    public async Task ApplyToTreeAsync(
        IEnumerable<FileSystemNode> roots,
        IFileSystemProvider? comparisonProvider = null,
        CancellationToken ct = default)
    {
        var stack = new Stack<FileSystemNode>(roots);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var node = stack.Pop();
            var evaluation = await EvaluateNodeAsync(node, comparisonProvider, ct);
            node.FilterResult = evaluation.IsIncluded ? FilterResult.Included : FilterResult.Excluded;
            node.ExcludedByFilter = evaluation.IsIncluded ? null : evaluation.ExcludedByFilter;

            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }
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

    /// <summary>Convenience overload that uses <see cref="FilterFactory.FromConfig"/>.</summary>
    public static FilterChain FromConfig(FilterChainConfig config)
        => FromConfig(config, FilterFactory.FromConfig);

    private async Task<NodeEvaluation> EvaluateNodeAsync(
        FileSystemNode node,
        IFileSystemProvider? comparisonProvider,
        CancellationToken ct)
    {
        bool inSet = true;
        string? excludedBy = null;
        IFilter? excludingFilter = null;

        foreach (var filter in _filters.Where(f => f.IsEnabled))
        {
            if (node.IsDirectory && !filter.AppliesToDirectories)
                continue;

            ct.ThrowIfCancellationRequested();
            var matches = await filter.MatchesAsync(node, comparisonProvider, ct);

            switch (filter.Mode)
            {
                case FilterMode.Only:
                    if (inSet && !matches)
                    {
                        inSet = false;
                        excludedBy = filter.Name;
                        excludingFilter = filter;
                    }
                    break;

                case FilterMode.Add:
                    if (matches)
                    {
                        inSet = true;
                        excludedBy = null;
                        excludingFilter = null;
                    }
                    break;

                case FilterMode.Exclude:
                    if (matches)
                    {
                        inSet = false;
                        excludedBy = filter.Name;
                        excludingFilter = filter;
                    }
                    break;
            }
        }

        return new NodeEvaluation(inSet, excludedBy, excludingFilter);
    }

    private readonly record struct NodeEvaluation(
        bool IsIncluded,
        string? ExcludedByFilter,
        IFilter? MatchingFilter);
}
