using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Filters;

public sealed class FilterChain
{
    private const string AllChildrenExcluded = "All children excluded";
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
        var postOrderList = new List<FileSystemNode>();

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var node = stack.Pop();
            postOrderList.Add(node);

            var evaluation = await EvaluateNodeAsync(node, comparisonProvider, ct);
            using (node.BeginBatchUpdate())
            {
                node.FilterResult = evaluation.IsIncluded ? FilterResult.Included : FilterResult.Excluded;
                node.ExcludedByFilter = evaluation.IsIncluded ? null : evaluation.ExcludedByFilter;
            }

            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }

            // Also evaluate all files in the current directory so that
            // parent exclusion recalculation can see their correct state.
            foreach (var file in node.Files)
            {
                var fileEval = await EvaluateNodeAsync(file, comparisonProvider, ct);
                using (file.BeginBatchUpdate())
                {
                    file.FilterResult = fileEval.IsIncluded ? FilterResult.Included : FilterResult.Excluded;
                    file.ExcludedByFilter = fileEval.IsIncluded ? null : fileEval.ExcludedByFilter;
                }
            }
        }

        for (var i = postOrderList.Count - 1; i >= 0; i--)
        {
            UpdateDirectoryExclusion(postOrderList[i]);
        }

        foreach (var parent in roots.Where(r => r.Parent != null).Select(r => r.Parent).Distinct())
        {
            RecalculateParentExclusion(parent);
        }
    }

    public static void RecalculateParentExclusion(FileSystemNode? node)
    {
        while (node != null)
        {
            UpdateDirectoryExclusion(node);
            node = node.Parent;
        }
    }

    /// <summary>
    /// If <paramref name="node"/> is a non-empty directory that was not explicitly excluded
    /// by a filter, sets its <see cref="FileSystemNode.FilterResult"/> based on whether
    /// any child or file is still included.
    /// </summary>
    private static void UpdateDirectoryExclusion(FileSystemNode node)
    {
        if (!node.IsDirectory || (node.Children.Count == 0 && node.Files.Count == 0))
            return;

        if (node.FilterResult != FilterResult.Included && node.ExcludedByFilter != AllChildrenExcluded)
            return;

        bool hasIncluded = node.Children.Any(c => c.FilterResult == FilterResult.Included) ||
                           node.Files.Any(f => f.FilterResult == FilterResult.Included);

        using (node.BeginBatchUpdate())
        {
            if (hasIncluded)
            {
                node.FilterResult = FilterResult.Included;
                node.ExcludedByFilter = null;
            }
            else
            {
                node.FilterResult = FilterResult.Excluded;
                node.ExcludedByFilter = AllChildrenExcluded;
            }
        }
    }

    public FilterChainConfig ToConfig(string name = "Default", string? description = null)
    {
        return new FilterChainConfig(
            Name: name,
            Description: description,
            Filters: [.. _filters.Select(filter => filter.Config)]);
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
                    if (inSet && matches)
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
