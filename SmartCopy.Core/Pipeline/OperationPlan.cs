namespace SmartCopy.Core.Pipeline;

public readonly record struct PlannedAction(
    string SourcePath,
    SourceResult SourceResult,
    string? DestinationPath,
    DestinationResult DestinationResult,
    int NumberOfFilesAffected,
    int NumberOfFoldersAffected,
    long InputBytes,
    long OutputBytes,
    int NumberOfFilesSkipped = 0,
    int NumberOfFoldersSkipped = 0);

public sealed class OperationPlan
{
    public required IReadOnlyList<PlannedAction> Actions { get; init; }
    public required long TotalInputBytes { get; init; }
    public required long TotalEstimatedOutputBytes { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> InfoMessages { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    /// <summary>Per-step copy/move transfer-strategy summaries, shown in the preview so the
    /// resolved policy decision is visible without running the operation.</summary>
    public IReadOnlyList<string> StrategyNotes { get; init; } = [];
    /// <summary>Plan-wide copy-optimisations status (routing on/off), shown alongside
    /// <see cref="StrategyNotes"/>. Empty when the plan has no byte-transfer steps.</summary>
    public string StrategyStatus { get; init; } = "";
    public int TotalFilesAffected   => Actions.Sum(a => a.NumberOfFilesAffected);
    public int TotalFoldersAffected => Actions.Sum(a => a.NumberOfFoldersAffected);
    public int TotalFilesSkipped    => Actions.Sum(a => a.NumberOfFilesSkipped);
    public int TotalFoldersSkipped  => Actions.Sum(a => a.NumberOfFoldersSkipped);
}
