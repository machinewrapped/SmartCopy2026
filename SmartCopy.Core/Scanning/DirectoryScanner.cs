using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Scanning;

public sealed class DirectoryScanner
{
    private readonly IFileSystemProvider _provider;

    public DirectoryScanner(IFileSystemProvider provider) => _provider = provider;

    public async IAsyncEnumerable<FileSystemNode> ScanAsync(
        string rootPath,
        ScanOptions options,
        IProgress<ScanProgress>? progress = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var rootNode = CloneForTree(await _provider.GetNodeAsync(rootPath, ct), parent: null, rootPath);
        yield return rootNode;

        // visited guards against circular symbolic links re-enqueueing an already-processed path.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { rootNode.FullPath };
        var queue = new Queue<(FileSystemNode Node, int Depth)>();
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

                var cloned = CloneForTree(child, currentDirectory, rootPath);
                if (cloned.IsDirectory)
                {
                    currentDirectory.Children.Add(cloned);
                    if (!options.LazyExpand && visited.Add(cloned.FullPath))
                    {
                        queue.Enqueue((cloned, depth + 1));
                    }
                }
                else
                {
                    currentDirectory.Files.Add(cloned);
                }

                nodesDiscovered++;
                progress?.Report(new ScanProgress(nodesDiscovered, directoriesScanned, cloned.FullPath));
                yield return cloned;
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

    private FileSystemNode CloneForTree(FileSystemNode source, FileSystemNode? parent, string rootPath)
    {
        return new FileSystemNode
        {
            Name = source.Name,
            FullPath = source.FullPath,
            RelativePathSegments = _provider.SplitPath(_provider.GetRelativePath(rootPath, source.FullPath)),
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
