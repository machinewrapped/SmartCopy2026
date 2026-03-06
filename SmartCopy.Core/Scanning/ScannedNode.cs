using SmartCopy.Core.FileSystem;

namespace SmartCopy.Core.Scanning;

public sealed record ScannedNode(FileSystemNode FileSystemNode, ScannedNode[] Children);
