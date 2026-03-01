namespace SmartCopy.Core.DirectoryTree;

public enum FilterResult
{
    Included, // ALL descendants filter-included (atomic-safe)
    Mixed,    // some included, some excluded (directories only)
    Excluded  // ALL descendants filter-excluded
}
