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

    internal static async Task<DirectoryTreeNode> BuildDirectoryTree(this MemoryFileSystemProvider provider, string? path = null)
    {
        path ??= provider.RootPath;

        FileSystemNode rootNode = await provider.GetNodeAsync(path, CancellationToken.None)
            ?? throw new InvalidOperationException($"Root path '{path}' does not exist in the file system fixture.");

        return await BuildDirectoryTreeNode(provider, rootNode, parent: null);
    }

    private static async Task<DirectoryTreeNode> BuildDirectoryTreeNode(MemoryFileSystemProvider provider, FileSystemNode entry, DirectoryTreeNode? parent)
    {
        var node = new DirectoryTreeNode(entry, parent);

        if (!entry.IsDirectory)
            return node;

        var children = await provider.GetChildrenAsync(entry.FullPath, CancellationToken.None);
        foreach (var child in children)
        {
            var childNode = await BuildDirectoryTreeNode(provider, child, parent: node);
            if (child.IsDirectory)
            {
                node.Children.Add(childNode);
            }
            else
            {
                node.Files.Add(childNode);
            }
        }

        return node;
    }
}
