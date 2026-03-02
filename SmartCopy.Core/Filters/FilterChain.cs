using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.DirectoryTree;

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

    public async Task<IReadOnlyList<DirectoryTreeNode>> ApplyAsync(
        IEnumerable<DirectoryTreeNode> nodes,
        CancellationToken ct = default)
    {
        var result = new List<DirectoryTreeNode>();
        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();
            var evaluation = await EvaluateNodeAsync(node, ct);
            if (evaluation.IsIncluded)
            {
                result.Add(node);
            }
        }

        return result;
    }

    public async Task ApplyToTreeAsync(
        IEnumerable<DirectoryTreeNode> roots,
        CancellationToken ct = default)
    {
        var stack = new Stack<DirectoryTreeNode>(roots);
        var postOrderList = new List<DirectoryTreeNode>();

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var node = stack.Pop();
            postOrderList.Add(node);

            var evaluation = await EvaluateNodeAsync(node, ct);
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
                var fileEval = await EvaluateNodeAsync(file, ct);
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

    public static void RecalculateParentExclusion(DirectoryTreeNode? node)
    {
        while (node != null)
        {
            UpdateDirectoryExclusion(node);
            node = node.Parent;
        }
    }

    /// <summary>
    /// For a non-empty directory, sets its <see cref="DirectoryTreeNode.FilterResult"/> based on
    /// whether any child or file is still included. A directory is only excluded when ALL its
    /// content is excluded; individual filter evaluation on the directory itself is overridden.
    /// </summary>
    private static void UpdateDirectoryExclusion(DirectoryTreeNode node)
    {
        if (!node.IsDirectory || (node.Children.Count == 0 && node.Files.Count == 0))
            return;

        bool allIncluded =
            node.Files.All(f => f.FilterResult == FilterResult.Included) &&
            node.Children.All(c => c.FilterResult == FilterResult.Included); // Mixed child → not allIncluded

        bool anyIncluded =
            node.Files.Any(f => f.FilterResult != FilterResult.Excluded) ||
            node.Children.Any(c => c.FilterResult != FilterResult.Excluded);

        using (node.BeginBatchUpdate())
        {
            node.FilterResult = allIncluded ? FilterResult.Included
                              : anyIncluded ? FilterResult.Mixed
                              : FilterResult.Excluded;
            node.ExcludedByFilter = anyIncluded ? null : AllChildrenExcluded;
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
        DirectoryTreeNode node,
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
            var matches = await filter.MatchesAsync(node, ct);

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
