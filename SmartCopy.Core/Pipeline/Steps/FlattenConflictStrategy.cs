namespace SmartCopy.Core.Pipeline.Steps;

public enum FlattenConflictStrategy
{
    AutoRenameCounter,
    AutoRenameSourcePath,
    Skip,
    Overwrite,
}
