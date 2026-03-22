using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SmartCopy.Core.Pipeline.Validation;
using SmartCopy.Core.Selection;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class SaveSelectionToFileStep : IPipelineStep
{
    public SaveSelectionToFileStep(string filePath, bool useAbsolutePaths = false)
    {
        FilePath = filePath;
        UseAbsolutePaths = useAbsolutePaths;
    }

    public string FilePath { get; }
    public bool UseAbsolutePaths { get; }

    public StepKind StepType => StepKind.SaveSelectionToFile;
    public bool IsExecutable => true;

    public string AutoSummary => StepType.ForDisplay();
    public string Description => string.IsNullOrWhiteSpace(FilePath) ? "No file specified" : $"→ {FilePath}";

    public TransformStepConfig Config => new(StepType, new JsonObject
    {
        ["filePath"]         = FilePath,
        ["useAbsolutePaths"] = UseAbsolutePaths.ToString(),
    });

    internal static SaveSelectionToFileStep FromConfig(TransformStepConfig config)
    {
        var filePath = config.GetOptional("filePath");
        var useAbsolutePaths = config.GetOptional("useAbsolutePaths")
            .Equals("true", StringComparison.OrdinalIgnoreCase);
        return new SaveSelectionToFileStep(filePath, useAbsolutePaths);
    }

    public Task Validate(StepValidationContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(FilePath))
            context.AddBlockingIssue("Step.MissingFilePath", "Save Selection to File requires a file path.");
        else
            context.ValidateHasSelectedInputs();

        // Post-condition: selection state unchanged
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        yield return new TransformResult(
            IsSuccess: true,
            SourceNode: context.RootNode,
            SourceNodeResult: SourceResult.None,
            ActionSummary: $"Will save current selection to {FilePath}");
    }

    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        var snapshot = new SelectionManager().Capture(context.RootNode, UseAbsolutePaths);
        await new SelectionSerializer().SaveAsync(FilePath, snapshot, ct);
        yield return new TransformResult(
            IsSuccess: true,
            SourceNode: context.RootNode,
            SourceNodeResult: SourceResult.None,
            ActionSummary: $"Saved current selection to {FilePath}");
    }
}
