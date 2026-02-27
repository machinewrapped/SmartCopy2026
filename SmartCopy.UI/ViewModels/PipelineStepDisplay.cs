using System.IO;
using SmartCopy.Core.Pipeline;
using SmartCopy.Core.Pipeline.Steps;

namespace SmartCopy.UI.ViewModels;

internal static class PipelineStepDisplay
{
    public static string GetDefaultTitle(StepKind kind)
    {
        return kind switch
        {
            StepKind.Copy => "Copy files",
            StepKind.Move => "Move files",
            StepKind.Delete => "Delete files",
            StepKind.Flatten => "Flatten folders",
            StepKind.Rename => "Rename files",
            StepKind.Rebase => "Rebase paths",
            StepKind.Convert => "Convert files",
            StepKind.SelectAll => "Select all",
            StepKind.InvertSelection => "Invert selection",
            StepKind.ClearSelection => "Clear selection",
            _ => "Step",
        };
    }

    public static string GetSummary(ITransformStep step)
    {
        return step switch
        {
            CopyStep copyStep => string.IsNullOrWhiteSpace(copyStep.DestinationPath)
                ? "Copy files"
                : $"Copy to {GetFriendlyTarget(copyStep.DestinationPath)}",
            MoveStep moveStep => string.IsNullOrWhiteSpace(moveStep.DestinationPath)
                ? "Move files"
                : $"Move to {GetFriendlyTarget(moveStep.DestinationPath)}",
            DeleteStep deleteStep => deleteStep.Mode == DeleteMode.Permanent
                ? "Delete permanently"
                : "Delete to Trash",
            FlattenStep => "Flatten folders",
            RenameStep => "Rename files",
            RebaseStep => "Rebase paths",
            ConvertStep => "Convert files",
            SelectAllStep => "Select all",
            InvertSelectionStep => "Invert selection",
            ClearSelectionStep => "Clear selection",
            _ => step.StepType.ToString(),
        };
    }

    public static string GetDescription(ITransformStep step)
    {
        return step switch
        {
            CopyStep copyStep => string.IsNullOrWhiteSpace(copyStep.DestinationPath)
                ? "Destination: required"
                : $"Destination: {copyStep.DestinationPath}",
            MoveStep moveStep => string.IsNullOrWhiteSpace(moveStep.DestinationPath)
                ? "Destination: required"
                : $"Destination: {moveStep.DestinationPath}",
            DeleteStep deleteStep => deleteStep.Mode == DeleteMode.Permanent
                ? "This cannot be undone."
                : string.Empty,
            FlattenStep flattenStep => $"Conflict strategy: {flattenStep.ConflictStrategy}",
            RenameStep renameStep => $"Pattern: {renameStep.Pattern}",
            RebaseStep rebaseStep => $"Strip: '{rebaseStep.StripPrefix}'  Add: '{rebaseStep.AddPrefix}'",
            ConvertStep convertStep => string.IsNullOrWhiteSpace(convertStep.OutputExtension)
                ? "Output extension: required"
                : $"Output extension: .{convertStep.OutputExtension}",
            SelectAllStep => "Mark all files as selected",
            InvertSelectionStep => "Toggle selection on each file",
            ClearSelectionStep => "Unmark all files",
            _ => step.StepType.ToString(),
        };
    }

    public static string NormalizeCustomName(string? customName)
    {
        return string.IsNullOrWhiteSpace(customName) ? string.Empty : customName.Trim();
    }

    private static string GetFriendlyTarget(string path)
    {
        var normalized = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "destination";
        }

        var leaf = Path.GetFileName(normalized);
        return string.IsNullOrWhiteSpace(leaf) ? normalized : leaf;
    }
}
