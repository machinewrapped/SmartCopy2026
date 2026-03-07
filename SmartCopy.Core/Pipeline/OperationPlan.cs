using System.Linq;

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
    public int TotalFilesAffected   => Actions.Sum(a => a.NumberOfFilesAffected);
    public int TotalFoldersAffected => Actions.Sum(a => a.NumberOfFoldersAffected);
    public int TotalFilesSkipped    => Actions.Sum(a => a.NumberOfFilesSkipped);
    public int TotalFoldersSkipped  => Actions.Sum(a => a.NumberOfFoldersSkipped);
}
