using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Threading;
using SmartCopy.Core.DirectoryTree;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class DeleteStep : IPipelineStep
{
    public DeleteStep(DeleteMode mode = DeleteMode.Trash)
    {
        Mode = mode;
    }

    public DeleteMode Mode { get; set; }

    public StepKind StepType => StepKind.Delete;
    public bool IsExecutable => true;

    public TransformStepConfig Config => new(StepType, new JsonObject { ["deleteMode"] = Mode.ToString() });

    public void Validate(StepValidationContext context)
    {
        context.ValidateHasSelectedInputs();
        context.ValidateSourceExists("Delete");
        context.SourceExists = false;
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        var pathResult = Mode == DeleteMode.Trash ? SourceResult.Trashed : SourceResult.Deleted;

        // Include root node itself if selected, then all selected descendants.
        if (context.IsPreviewSelected(context.RootNode))
        {
            yield return MakePreviewResult(context.RootNode, pathResult);
        }

        // Yield all affected nodes for preview so the user sees exactly what will be deleted
        foreach (var node in context.GetPreviewSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;
            yield return MakePreviewResult(node, pathResult);
        }
    }

    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var pathResult = Mode == DeleteMode.Trash ? SourceResult.Trashed : SourceResult.Deleted;

        // If the root node itself is fully selected, delete it atomically.
        if (context.RootNode.IsSelected)
        {
            await context.SourceProvider.DeleteAsync(context.RootNode.FullPath, ct);
            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: context.RootNode,
                SourceNodeResult: pathResult,
                NumberOfFilesAffected: context.RootNode.CountAllFiles(),
                NumberOfFoldersAffected: context.RootNode.CountAllFolders(),
                InputBytes: context.RootNode.Size);
            yield break;
        }

        foreach (var node in context.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;
            // Skip nodes whose parent is also selected — the parent delete covers them.
            if (node.Parent?.IsSelected == true) continue;

            await context.SourceProvider.DeleteAsync(node.FullPath, ct);
            yield return new TransformResult(
                IsSuccess: true,
                SourceNode: node,
                SourceNodeResult: pathResult,
                NumberOfFilesAffected: node.CountAllFiles(),
                NumberOfFoldersAffected: node.CountAllFolders(),
                InputBytes: node.Size);
        }
    }

    private static TransformResult MakePreviewResult(
        DirectoryTreeNode node, SourceResult pathResult)
        => new(
            IsSuccess: true,
            SourceNode: node,
            SourceNodeResult: pathResult,
            NumberOfFilesAffected: node.IsDirectory ? 0 : 1,
            NumberOfFoldersAffected: node.IsDirectory ? 1 : 0,
            InputBytes: node.Size);
}
