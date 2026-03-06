using System;

namespace SmartCopy.Core.Selection;

public sealed class SelectionSnapshot
{
    private readonly HashSet<string> _paths;

    public SelectionSnapshot(IEnumerable<string> paths)
    {
        _paths = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The paths stored in this snapshot. May be relative or absolute depending on how the
    /// snapshot was captured (see <see cref="SelectionManager.Capture"/>).
    /// </summary>
    public IReadOnlyCollection<string> Paths => _paths;

    public bool Contains(string path) => _paths.Contains(path);
}

