namespace SmartCopy.Core.FileSystem;

/// <summary>
/// Tracks the last directory a provider <em>freshly created</em> during a bulk write, so consecutive
/// writes into the same new directory can skip redundant existence checks and creation calls.
/// <para>
/// Only directories created from scratch are recorded — a pre-existing directory may already contain
/// files, so it is never assumed empty. The owning provider performs the actual create/exists I/O;
/// this type holds only the cached state and its invalidation lifetime (reset at bulk-write
/// boundaries). It is the reusable, storage-agnostic half of the optimisation; the I/O half stays
/// provider-specific.
/// </para>
/// </summary>
internal sealed class FreshDirectoryTracker(StringComparison comparison)
{
    private string? _lastCreated;

    /// <summary>Clears the tracked directory. Call at bulk-write boundaries (begin and dispose).</summary>
    public void Reset() => _lastCreated = null;

    /// <summary>
    /// True when <paramref name="directory"/> is the directory this tracker most recently created,
    /// and is therefore known to be empty.
    /// </summary>
    public bool IsFreshlyCreated(string? directory) =>
        _lastCreated is not null && directory is not null && string.Equals(directory, _lastCreated, comparison);

    /// <summary>Records <paramref name="directory"/> as freshly created (hence empty).</summary>
    public void MarkCreated(string directory) => _lastCreated = directory;
}
