namespace SmartCopy.Core.FileSystem;

/// <summary>
/// Tracks the last directory a provider verified during a bulk write, so consecutive writes into
/// the same directory can skip redundant parent-directory checks and creation calls.
/// <para>
/// Freshly-created directories are tracked separately from merely-known directories: a fresh
/// directory is known to be empty, while a pre-existing directory is only known to exist. The owning
/// provider performs the actual create/exists I/O; this type holds only the cached state and its
/// invalidation lifetime (reset at bulk-write boundaries). It is the reusable, storage-agnostic half
/// of the optimisation; the I/O half stays provider-specific.
/// </para>
/// </summary>
internal sealed class FreshDirectoryTracker(StringComparison comparison)
{
    private string? _lastKnown;
    private string? _lastCreated;

    /// <summary>Clears the tracked directory. Call at bulk-write boundaries (begin and dispose).</summary>
    public void Reset()
    {
        _lastKnown = null;
        _lastCreated = null;
    }

    /// <summary>
    /// True when <paramref name="directory"/> is the directory this tracker most recently verified
    /// or created, and is therefore known to exist.
    /// </summary>
    public bool IsKnown(string? directory) =>
        _lastKnown is not null && directory is not null && string.Equals(directory, _lastKnown, comparison);

    /// <summary>
    /// True when <paramref name="directory"/> is the directory this tracker most recently created,
    /// and is therefore known to be empty.
    /// </summary>
    public bool IsFreshlyCreated(string? directory) =>
        _lastCreated is not null && directory is not null && string.Equals(directory, _lastCreated, comparison);

    /// <summary>Records <paramref name="directory"/> as known to exist, without assuming it is empty.</summary>
    public void MarkKnown(string directory) => _lastKnown = directory;

    /// <summary>Records <paramref name="directory"/> as freshly created (hence empty).</summary>
    public void MarkCreated(string directory)
    {
        _lastKnown = directory;
        _lastCreated = directory;
    }
}
