using SmartCopy.Core.DirectoryTree;
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

    public static FilterChain Empty => new([]);

    public async Task<IReadOnlyList<DirectoryTreeNode>> ApplyAsync(
        IEnumerable<DirectoryTreeNode> nodes,
        IPathResolver? context = null,
        CancellationToken ct = default)
    {
        var resolvedContext = context ?? FileSystemProviderRegistry.Empty;
        var result = new List<DirectoryTreeNode>();
        foreach (var node in nodes)
        {
            ct.ThrowIfCancellationRequested();
            var evaluation = await EvaluateNodeAsync(node, resolvedContext, ct);
            if (evaluation == FilterResult.Included)
            {
                result.Add(node);
            }
        }

        return result;
    }

    public async Task ApplyToTreeAsync(
        DirectoryNode root,
        IPathResolver? context = null,
        CancellationToken ct = default)
    {
        var resolvedContext = context ?? FileSystemProviderRegistry.Empty;
        var stack = new Stack<DirectoryNode>([root]);
        var postOrderList = new List<DirectoryNode>();

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var node = stack.Pop();
            postOrderList.Add(node);

            node.FilterResult = await EvaluateNodeAsync(node, resolvedContext, ct);

            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }

            // Also evaluate all files in the current directory so that
            // parent exclusion recalculation can see their correct state.
            foreach (var file in node.Files)
            {
                file.FilterResult = await EvaluateNodeAsync(file, resolvedContext, ct);
            }
        }

        for (var i = postOrderList.Count - 1; i >= 0; i--)
        {
            UpdateDirectoryExclusion(postOrderList[i]);
        }

        // Roots really shouldn't have a parent, but just in case...
        if (root.Parent != null)
        {
            RecalculateParentExclusion(root.Parent);
        }
    }

    public static void RecalculateParentExclusion(DirectoryNode? node)
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
    private static void UpdateDirectoryExclusion(DirectoryNode node)
    {
        if (node.Children.Count == 0 && node.Files.Count == 0)
            return;

        bool allIncluded =
            node.Files.All(f => f.FilterResult == FilterResult.Included) &&
            node.Children.All(c => c.FilterResult == FilterResult.Included); // Mixed child → not allIncluded

        bool anyIncluded =
            node.Files.Any(f => f.FilterResult != FilterResult.Excluded) ||
            node.Children.Any(c => c.FilterResult != FilterResult.Excluded);

        node.FilterResult = allIncluded ? FilterResult.Included
                            : anyIncluded ? FilterResult.Mixed
                            : FilterResult.Excluded;
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

    private async Task<FilterResult> EvaluateNodeAsync(DirectoryTreeNode node, IPathResolver context, CancellationToken ct)
    {
        FilterResult result = FilterResult.Included;

        foreach (var filter in _filters.Where(f => f.IsEnabled))
        {
            if (node is DirectoryNode && !filter.AppliesToDirectories)
                continue;

            ct.ThrowIfCancellationRequested();
            var matches = await filter.MatchesAsync(node, context, ct);

            switch (filter.Mode)
            {
                case FilterMode.Only:
                    if (!matches)
                    {
                        result = FilterResult.Excluded;
                    }
                    break;

                case FilterMode.Add:
                    if (matches)
                    {
                        result = FilterResult.Included;
                    }
                    break;

                case FilterMode.Exclude:
                    if (matches)
                    {
                        result = FilterResult.Excluded;
                    }
                    break;
            }
        }

        return result;
    }
}
