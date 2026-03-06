namespace SmartCopy.Core.FileSystem;

public readonly record struct ProviderCapabilities(
    bool CanSeek,
    bool CanAtomicMove,
    bool CanWatch,
    int MaxPathLength);
