using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.Core.Pipeline;

public static class PipelineHelpers
{
    public static int GetTotalFileCount(DirectoryTreeNode node) => node switch
    {
        DirectoryNode dir => dir.CountAllFiles(),
        FileNode => 1,
        _ => 0
    };

    public static int GetTotalFolderCount(DirectoryTreeNode node) => node switch
    {
        DirectoryNode dir => dir.CountAllFolders(),
        _ => 0
    };

    public static int GetSelectedFileCount(DirectoryTreeNode node) => node switch
    {
        DirectoryNode dir => dir.CountSelectedFiles(),
        FileNode file => file.IsSelected ? 1 : 0,
        _ => 0
    };

    public static int GetSelectedFolderCount(DirectoryTreeNode node) => node switch
    {
        DirectoryNode dir => dir.CountSelectedFolders(),
        _ => 0
    };
}
