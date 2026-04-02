namespace SmartCopy.Core.Settings;

public sealed class ImportManifest
{
    public required IReadOnlyList<string> NewFiles { get; init; }
    public required IReadOnlyList<string> ConflictingFiles { get; init; }
    public required int TotalEntries { get; init; }
}

public enum ConflictResolution
{
    OverwriteAll,
    SkipExisting,
}
