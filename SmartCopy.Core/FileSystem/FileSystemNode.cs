using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SmartCopy.Core.FileSystem;

public sealed class FileSystemNode
{
    // Filesystem data (immutable after scan)
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;

    /// <summary>
    /// The path expressed as separator-free segments, set by the source provider at scan time.
    /// </summary>
    public string[] PathSegments { get; init; } = [];

    /// <summary>
    /// The relative path as a canonical forward-slash string, derived from <see cref="PathSegments"/>.
    /// </summary>
    public string CanonicalPath => string.Join("/", PathSegments);

    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ModifiedAt { get; init; }
    public FileAttributes Attributes { get; init; }
}
