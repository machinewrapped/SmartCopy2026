using System;
using System.Collections.Generic;

namespace SmartCopy.Core.Selection;

public sealed class SelectionSnapshot
{
    private readonly HashSet<string> _relativePaths;

    public SelectionSnapshot(IEnumerable<string> relativePaths)
    {
        _relativePaths = new HashSet<string>(relativePaths, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> RelativePaths => _relativePaths;

    public bool Contains(string relativePath) => _relativePaths.Contains(relativePath);
}

