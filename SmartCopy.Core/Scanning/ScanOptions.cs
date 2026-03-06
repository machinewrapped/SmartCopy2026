namespace SmartCopy.Core.Scanning;

public sealed class ScanOptions
{
    public bool IncludeHidden { get; init; }
    public bool FullPreScan { get; init; }
    public bool LazyExpand { get; init; }
    public bool FollowSymlinks { get; init; }
    public int? MaxDepth { get; init; }
}

