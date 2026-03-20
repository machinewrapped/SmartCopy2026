using System.Runtime.CompilerServices;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Scanning;

public sealed class DirectoryScanner
{
    private readonly IFileSystemProvider _provider;

    public DirectoryScanner(IFileSystemProvider provider) => _provider = provider;

    public async IAsyncEnumerable<DirectoryTreeNode> ScanAsync(
        string rootPath,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var rootFsNode = await _provider.GetNodeAsync(rootPath, ct);
        var rootNode = new DirectoryNode(rootFsNode, parent: null);

        yield return rootNode;

        if (!rootNode.IsDirectory) yield break;

        // visited guards against circular symbolic links re-enqueueing an already-processed path.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootNode.FullPath };
        var queue = new Queue<(DirectoryNode Node, int Depth)>();
        queue.Enqueue((rootNode, 0));

        var directoriesScanned = 0;
        var nodesDiscovered = 0;

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (currentDirectory, depth) = queue.Dequeue();

            if (options.MaxDepth.HasValue && depth > options.MaxDepth.Value)
            {
                continue;
            }

            var children = await _provider.GetChildrenAsync(currentDirectory.FullPath, ct);
            directoriesScanned++;
            progress?.Report(new ScanProgress(nodesDiscovered, directoriesScanned, currentDirectory.FullPath));

            foreach (var child in children)
            {
                ct.ThrowIfCancellationRequested();
                if (!ShouldIncludeNode(child, options))
                {
                    continue;
                }

                // User may select parent whilst scan is in progress... propagate the check state to new additions
                CheckState initialCheckstate = currentDirectory.CheckState == CheckState.Checked ? CheckState.Checked : CheckState.Unchecked;

                DirectoryTreeNode node;
                if (child.IsDirectory)
                {
                    var dirNode = new DirectoryNode(child, currentDirectory, initialCheckstate);
                    currentDirectory.Children.Add(dirNode);

                    if (!options.LazyExpand && ShouldTraverse(options, child) && visited.Add(dirNode.FullPath))
                    {
                        queue.Enqueue((dirNode, depth + 1));
                    }

                    node = dirNode;
                }
                else
                {
                    var fileNode = new FileNode(child, currentDirectory, initialCheckstate);
                    currentDirectory.Files.Add(fileNode);
                    node = fileNode;
                }

                nodesDiscovered++;
                progress?.Report(new ScanProgress(nodesDiscovered, directoriesScanned, node.FullPath));
                yield return node;
            }
        }
    }

    private static bool ShouldTraverse(ScanOptions options, FileSystemNode child) 
        => options.FollowSymlinks || !IsReparsePoint(child);
    
    public async Task<ScannedNode> BuildScannedSubtreeAsync(
        string rootPath,
        ScanOptions options,
        CancellationToken ct = default)
    {
        var root = await _provider.GetNodeAsync(rootPath, ct);
        return await BuildScannedNodeAsync(root, options, 0, ct);
    }

    private async Task<ScannedNode> BuildScannedNodeAsync(
        FileSystemNode node,
        ScanOptions options,
        int depth,
        CancellationToken ct)
    {
        if (!node.IsDirectory || !ShouldTraverse(options, node) || (options.MaxDepth.HasValue && depth >= options.MaxDepth.Value))
            return new ScannedNode(node, []);

        var rawChildren = await _provider.GetChildrenAsync(node.FullPath, ct);
        var children = new List<ScannedNode>();
        foreach (var child in rawChildren)
        {
            if (!ShouldIncludeNode(child, options)) continue;
            if (child.IsDirectory)
                children.Add(await BuildScannedNodeAsync(child, options, depth + 1, ct));
            else
                children.Add(new ScannedNode(child, []));
        }
        return new ScannedNode(node, [.. children]);
    }

    private static bool ShouldIncludeNode(FileSystemNode node, ScanOptions options)
    {
        if (options.IncludeHidden)
        {
            return true;
        }

        return (node.Attributes & FileAttributes.Hidden) == 0;
    }

    private static bool IsReparsePoint(FileSystemNode node) =>
        (node.Attributes & FileAttributes.ReparsePoint) != 0;

}
