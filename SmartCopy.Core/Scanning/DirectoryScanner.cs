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
        var rootNode = new DirectoryTreeNode(
            await _provider.GetNodeAsync(rootPath, ct), parent: null);

        yield return rootNode;

        // visited guards against circular symbolic links re-enqueueing an already-processed path.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootNode.FullPath };
        var queue = new Queue<(DirectoryTreeNode Node, int Depth)>();
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

                var node = new DirectoryTreeNode(child, currentDirectory, initialCheckstate);
                if (node.IsDirectory)
                {
                    currentDirectory.Children.Add(node);
                    if (!options.LazyExpand && visited.Add(node.FullPath))
                    {
                        queue.Enqueue((node, depth + 1));
                    }
                }
                else
                {
                    currentDirectory.Files.Add(node);
                }

                nodesDiscovered++;
                progress?.Report(new ScanProgress(nodesDiscovered, directoriesScanned, node.FullPath));
                yield return node;
            }
        }
    }

    private static bool ShouldIncludeNode(FileSystemNode node, ScanOptions options)
    {
        if (options.IncludeHidden)
        {
            return true;
        }

        return (node.Attributes & FileAttributes.Hidden) == 0;
    }
}
