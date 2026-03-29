using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SmartCopy.Core.DirectoryTree;
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

    public async Task Validate(StepValidationContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            context.AddBlockingIssue("Step.MissingFilePath", "Add Selection from File requires a file path.");
            return;
        }

        if (context.ProviderRegistry?.ResolveProvider(FilePath) is not { } provider || !await provider.ExistsAsync(FilePath, ct))
            context.AddBlockingIssue("Step.FileNotFound", $"Selection file not found: {FilePath}");

        // Post-condition: conservative — assume all files could now be selected
        context.SourceExists = true;
        context.SelectedFileCount = context.NumFilterIncludedFiles;
        context.SelectedBytes = context.TotalFilterIncludedBytes;
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        SelectionSnapshot? snapshot = null;
        try { snapshot = await new SelectionSerializer().LoadAsync(FilePath, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* snapshot stays null → error result below */ }

        if (snapshot is null)
        {
            yield return new TransformResult(
                IsSuccess: false,
                SourceNode: context.RootNode,
                SourceNodeResult: SourceResult.None,
                ErrorMessage: $"Add Selection from File: '{FilePath}' could not be read — the file may be corrupt or in an unsupported format");
            yield break;
        }

        foreach (var node in context.RootNode.GetFilterIncludedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (node is FileNode fileNode
                && (snapshot.Contains(fileNode.CanonicalRelativePath) || snapshot.Contains(fileNode.FullPath)))
                context.GetNodeContext(fileNode).VirtualCheckState = CheckState.Checked;
        }

        yield return new TransformResult(
            IsSuccess: true,
            SourceNode: context.RootNode,
            SourceNodeResult: SourceResult.None,
            ActionSummary: $"Will add files from {FilePath} to selection");
    }

    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        SelectionSnapshot snapshot;
        try { snapshot = await new SelectionSerializer().LoadAsync(FilePath, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new PipelineStepException(
                stepName: "Add Selection from File",
                userMessage: $"'{FilePath}' could not be read — the file may be corrupt or in an unsupported format",
                innerException: ex);
        }
        new SelectionManager().AddFromSnapshot(context.RootNode, snapshot);
        context.RootNode.BuildStats();
        yield return new TransformResult(
            IsSuccess: true,
            SourceNode: context.RootNode,
            SourceNodeResult: SourceResult.None,
            ActionSummary: $"Added files from {FilePath} to selection");
    }
}
