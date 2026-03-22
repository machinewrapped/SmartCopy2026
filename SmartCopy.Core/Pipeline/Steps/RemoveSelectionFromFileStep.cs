using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Pipeline.Validation;
using SmartCopy.Core.Selection;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class RemoveSelectionFromFileStep : IPipelineStep
{
    public RemoveSelectionFromFileStep(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }

    public StepKind StepType => StepKind.RemoveSelectionFromFile;
    public bool IsExecutable => true;

    public string AutoSummary => StepType.ForDisplay();
    public string Description => string.IsNullOrWhiteSpace(FilePath) ? "No file specified" : $"← {FilePath}";

    public TransformStepConfig Config => new(StepType, new JsonObject { ["filePath"] = FilePath });

    internal static RemoveSelectionFromFileStep FromConfig(TransformStepConfig config)
        => new(config.GetOptional("filePath"));

    public Task Validate(StepValidationContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            context.AddBlockingIssue("Step.MissingFilePath", "Remove Selection from File requires a file path.");
            return Task.CompletedTask;
        }

        if (!File.Exists(FilePath))
            context.AddBlockingIssue("Step.FileNotFound", $"Selection file not found: {FilePath}");

        // Post-condition: conservative — assume all matching files are now deselected
        context.SourceExists = true;
        context.SelectedFileCount = 0;
        context.SelectedBytes = 0;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        SelectionSnapshot? snapshot = null;
        try { snapshot = await new SelectionSerializer().LoadAsync(FilePath, ct); }
        catch { /* file not accessible during preview — skip matching */ }

        foreach (var node in context.RootNode.GetFilterIncludedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (snapshot is not null && node is FileNode fileNode)
            {
                var nodeCtx = context.GetNodeContext(fileNode);
                if (snapshot.Contains(fileNode.CanonicalRelativePath) || snapshot.Contains(fileNode.FullPath))
                    nodeCtx.VirtualCheckState = CheckState.Unchecked;
            }

            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.None);
        }
    }

    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        var snapshot = await new SelectionSerializer().LoadAsync(FilePath, ct);
        new SelectionManager().RemoveFromSnapshot(context.RootNode, snapshot);
        context.RootNode.BuildStats();

        foreach (var node in context.RootNode.GetFilterIncludedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: SourceResult.None);
        }
    }
}
