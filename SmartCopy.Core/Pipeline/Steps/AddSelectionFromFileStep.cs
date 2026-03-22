using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SmartCopy.Core.Pipeline.Validation;
using SmartCopy.Core.Selection;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class AddSelectionFromFileStep : IPipelineStep
{
    public AddSelectionFromFileStep(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public StepKind StepType => StepKind.AddSelectionFromFile;
    public bool IsExecutable => true;

    public string AutoSummary => StepType.ForDisplay();
    public string Description => string.IsNullOrWhiteSpace(FilePath) ? "No file specified" : $"← {FilePath}";

    public TransformStepConfig Config => new(StepType, new JsonObject { ["filePath"] = FilePath });

    internal static AddSelectionFromFileStep FromConfig(TransformStepConfig config)
        => new(config.GetOptional("filePath"));

    public Task Validate(StepValidationContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            context.AddBlockingIssue("Step.MissingFilePath", "Add Selection from File requires a file path.");
            return Task.CompletedTask;
        }

        if (!File.Exists(FilePath))
            context.AddBlockingIssue("Step.FileNotFound", $"Selection file not found: {FilePath}");

        // Post-condition: conservative — assume all files could now be selected
        context.SourceExists = true;
        context.SelectedFileCount = context.NumFilterIncludedFiles;
        context.SelectedBytes = context.TotalFilterIncludedBytes;
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
            ActionSummary: $"Will add files from {FilePath} to selection");
    }

    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        var snapshot = await new SelectionSerializer().LoadAsync(FilePath, ct);
        new SelectionManager().AddFromSnapshot(context.RootNode, snapshot);
        context.RootNode.BuildStats();
        yield return new TransformResult(
            IsSuccess: true,
            SourceNode: context.RootNode,
            SourceNodeResult: SourceResult.None,
            ActionSummary: $"Added files from {FilePath} to selection");
    }
}
