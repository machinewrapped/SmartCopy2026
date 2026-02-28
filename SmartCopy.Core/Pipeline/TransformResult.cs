namespace SmartCopy.Core.Pipeline;

public enum SourcePathResult { None, Copied, Moved, Trashed, Deleted }

public enum DestinationPathResult { None, Created, Overwritten }

public readonly record struct TransformResult(
    bool IsSuccess,
    string SourcePath,
    SourcePathResult SourcePathResult,
    string? DestinationPath                     = null,
    DestinationPathResult DestinationPathResult = DestinationPathResult.None,
    int NumberOfFilesAffected                   = 0,
    int NumberOfFoldersAffected                 = 0,
    long InputBytes                             = 0,
    long OutputBytes                            = 0);
