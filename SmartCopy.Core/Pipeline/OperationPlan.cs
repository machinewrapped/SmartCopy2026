using System.Collections.Generic;
using System.Linq;

namespace SmartCopy.Core.Pipeline;

public enum PlanWarning
{
    DestinationExists,
    NameConflict,
    PermissionIssue,
}

public readonly record struct PlannedAction(
    string StepSummary,
    string SourcePath,
    string DestinationPath,
    long InputBytes,
    long EstimatedOutputBytes,
    PlanWarning? Warning);

public sealed class OperationPlan
{
    public required IReadOnlyList<PlannedAction> Actions { get; init; }
    public required long TotalInputBytes { get; init; }
    public required long TotalEstimatedOutputBytes { get; init; }
    public int WarningCount => Actions.Count(action => action.Warning.HasValue);
}
