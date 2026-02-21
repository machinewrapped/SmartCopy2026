namespace SmartCopy.Core.Scanning;

public readonly record struct ScanProgress(
    int NodesDiscovered,
    int DirectoriesScanned,
    string CurrentPath);

