namespace SmartCopy.Core.FileSystem;

public readonly record struct ProviderCapabilities(
    bool CanSeek,
    bool CanAtomicMove,
    int MaxPathLength);

