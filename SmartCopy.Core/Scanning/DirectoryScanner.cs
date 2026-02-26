using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Scanning;

public sealed class DirectoryScanner
{
    public async IAsyncEnumerable<FileSystemNode> ScanAsync(
        IFileSystemProvider provider,
        string rootPath,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var rootNode = await provider.GetNodeAsync(rootPath, ct);

        var directoriesScanned = 0;
        var nodesDiscovered = 0;

        var topLevelChildren = await provider.GetChildrenAsync(rootNode.FullPath, ct);
        directoriesScanned++;
        progress?.Report(new ScanProgress(nodesDiscovered, directoriesScanned, rootNode.FullPath));

        var queue = new Queue<(FileSystemNode Node, int Depth)>();
        foreach (var child in topLevelChildren)
        {
            ct.ThrowIfCancellationRequested();
            if (!ShouldIncludeNode(child, options))
            {
                continue;
            }

            var rootedChild = CloneForTree(child, rootNode);
            rootNode.Children.Add(rootedChild);
            nodesDiscovered++;
            progress?.Report(new ScanProgress(nodesDiscovered, directoriesScanned, rootedChild.FullPath));
            yield return rootedChild;

            if (rootedChild.IsDirectory && !options.LazyExpand)
            {
                queue.Enqueue((rootedChild, 1));
            }
        }

        while (queue.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (currentDirectory, depth) = queue.Dequeue();

            if (options.MaxDepth.HasValue && depth > options.MaxDepth.Value)
            {
                continue;
            }

            var children = await provider.GetChildrenAsync(currentDirectory.FullPath, ct);
            directoriesScanned++;
            progress?.Report(new ScanProgress(nodesDiscovered, directoriesScanned, currentDirectory.FullPath));

            foreach (var child in children)
            {
                ct.ThrowIfCancellationRequested();
                if (!ShouldIncludeNode(child, options))
                {
                    continue;
                }

                var childWithParent = CloneForTree(child, currentDirectory);
                currentDirectory.Children.Add(childWithParent);
                nodesDiscovered++;
                progress?.Report(new ScanProgress(nodesDiscovered, directoriesScanned, childWithParent.FullPath));
                yield return childWithParent;

                if (childWithParent.IsDirectory)
                {
                    queue.Enqueue((childWithParent, depth + 1));
                }
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

    private static FileSystemNode CloneForTree(FileSystemNode source, FileSystemNode parent)
    {
        return new FileSystemNode
        {
            Name = source.Name,
            FullPath = source.FullPath,
            RelativePath = source.RelativePath,
            RelativePathSegments = source.RelativePathSegments,
            IsDirectory = source.IsDirectory,
            Size = source.Size,
            CreatedAt = source.CreatedAt,
            ModifiedAt = source.ModifiedAt,
            Attributes = source.Attributes,
            Parent = parent,
            CheckState = source.CheckState,
            FilterResult = source.FilterResult,
            ExcludedByFilter = source.ExcludedByFilter,
            Notes = source.Notes,
        };
    }
}

