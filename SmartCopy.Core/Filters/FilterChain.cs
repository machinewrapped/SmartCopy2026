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

    /// <summary>
    /// Evaluates <paramref name="root"/>'s subtree, committing each result once it is final: a file
    /// as soon as it is evaluated, a directory once its whole subtree is. A slow apply (mirror
    /// checks over a network) therefore updates the tree progressively, without ever showing a
    /// directory in a provisional state. If the apply is cancelled part-way, each unfinished
    /// directory is re-aggregated from whatever its contents now hold.
    /// </summary>
    public async Task ApplyToTreeAsync(
        DirectoryNode root,
        IPathResolver? context = null,
        CancellationToken ct = default)
    {
        var resolvedContext = context ?? FileSystemProviderRegistry.Empty;

        // Iterative post-order traversal: recursion could overflow the stack on a deep tree.
        // `open` holds the directories entered but not finished — always the path from the root
        // to the directory being evaluated — each with the children it has still to visit.
        var open = new Stack<(DirectoryNode Node, Queue<DirectoryNode> Children)>();
        DirectoryNode? next = root;

        try
        {
            while (next is not null || open.Count > 0)
            {
                if (next is null)
                {
                    var (dir, children) = open.Peek();
                    if (!children.TryDequeue(out next))
                    {
                        open.Pop();
                        UpdateDirectoryExclusion(dir);
                    }
                    continue;
                }

                ct.ThrowIfCancellationRequested();
                var node = next;
                next = null;

                // A directory with content takes its result from that content (UpdateDirectoryExclusion),
                // so its own evaluation only matters when it is empty.
                if (node.Children.Count == 0 && node.Files.Count == 0)
                {
                    node.FilterResult = await EvaluateNodeAsync(node, resolvedContext, ct);
                    continue;
                }

                // Snapshots: the watcher can patch these collections while evaluation is awaited.
                open.Push((node, new Queue<DirectoryNode>(node.Children.ToArray())));
                foreach (var file in node.Files.ToArray())
                {
                    file.FilterResult = await EvaluateNodeAsync(file, resolvedContext, ct);
                }
            }
        }
        finally
        {
            // Empty after a full pass; after a cancellation, re-aggregate deepest first.
            while (open.Count > 0)
            {
                UpdateDirectoryExclusion(open.Pop().Node);
            }

            // Applying to a subtree (e.g. the selected folder) changes what its ancestors contain.
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
