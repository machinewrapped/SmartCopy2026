using SmartCopy.Core.DirectoryTree;

namespace SmartCopy.Core.Pipeline;

public enum SourceResult { None, Copied, Moved, Trashed, Deleted }

public enum DestinationResult { None, Created, Overwritten }

public readonly record struct TransformResult(
    bool IsSuccess,
    DirectoryTreeNode SourceNode,
    SourceResult SourceNodeResult,
    string? DestinationPath                     = null,
    DestinationResult DestinationResult         = DestinationResult.None,
    int NumberOfFilesAffected                   = 0,
    int NumberOfFoldersAffected                 = 0,
    long InputBytes                             = 0,
    long OutputBytes                            = 0);
