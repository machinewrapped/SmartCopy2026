using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.FileSystem;

namespace SmartCopy.Tests.TestInfrastructure;

internal static class MemoryFilesystemExtensions
{
    internal static FileSystemProviderRegistry CreateRegistry(this MemoryFileSystemProvider provider)
    {
        var registry = new FileSystemProviderRegistry();
        registry.Register(provider);
        return registry;
    }

    internal static async Task<DirectoryNode> BuildDirectoryTree(this MemoryFileSystemProvider provider, string? path = null)
    {
        path ??= provider.RootPath;

        FileSystemNode rootNode = await provider.GetNodeAsync(path, CancellationToken.None)
            ?? throw new InvalidOperationException($"Root path '{path}' does not exist in the file system fixture.");

        if (!rootNode.IsDirectory)
            throw new ArgumentException($"Path '{path}' is not a directory.");

        return (DirectoryNode)await BuildDirectoryTreeNode(provider, rootNode, parent: null);
    }

    private static async Task<DirectoryTreeNode> BuildDirectoryTreeNode(MemoryFileSystemProvider provider, FileSystemNode entry, DirectoryNode? parent)
    {
        if (entry.IsDirectory)
        {
            var dirNode = new DirectoryNode(entry, parent);
            var children = await provider.GetChildrenAsync(entry.FullPath, CancellationToken.None);
            foreach (var child in children)
            {
                var childNode = await BuildDirectoryTreeNode(provider, child, parent: dirNode);
                if (childNode is DirectoryNode subDir)
                {
                    dirNode.Children.Add(subDir);
                }
                else if (childNode is FileNode file)
                {
                    dirNode.Files.Add(file);
                }
            }
            return dirNode;
        }
        else
        {
            return new FileNode(entry, parent);
        }
    }
}
