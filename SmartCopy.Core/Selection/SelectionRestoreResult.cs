namespace SmartCopy.Core.Selection;

public sealed record SelectionRestoreResult(int MatchedCount, IReadOnlyList<string> UnmatchedPaths)
{
    public bool HasUnmatched => UnmatchedPaths.Count > 0;
}
