using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
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

    public string AutoSummary => Mode == DeleteMode.Permanent ? "Delete" : "Trash";
    public string Description => Mode == DeleteMode.Permanent ? "Delete permanently" : "Delete to Trash";

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
            if (!context.AllowDeleteReadOnly && (context.RootNode.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                context.MarkFailed(context.RootNode);
                yield return MakePreviewResult(context.RootNode, SourceResult.None, isSuccess: false);
            }
            else
            {
                yield return MakePreviewResult(context.RootNode, pathResult);
            }
        }

        // Yield all affected nodes for preview so the user sees exactly what will be deleted
        foreach (var node in context.GetPreviewSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;

            if (!context.AllowDeleteReadOnly && (node.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                context.MarkFailed(node);
                yield return MakePreviewResult(node, SourceResult.None, isSuccess: false);
                continue;
            }

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
            if (!context.AllowDeleteReadOnly && (context.RootNode.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                context.MarkFailed(context.RootNode);
                yield return new TransformResult(
                    IsSuccess: false,
                    SourceNode: context.RootNode,
                    SourceNodeResult: SourceResult.None,
                    NumberOfFilesAffected: 0,
                    NumberOfFoldersAffected: 0,
                    InputBytes: context.RootNode.Size);
                yield break;
            }

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

            if (!context.AllowDeleteReadOnly && (node.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                context.MarkFailed(node);
                yield return new TransformResult(
                    IsSuccess: false,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.None,
                    NumberOfFilesAffected: 0,
                    NumberOfFoldersAffected: 0,
                    InputBytes: node.Size);
                continue;
            }

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
        DirectoryTreeNode node, SourceResult pathResult, bool isSuccess = true)
        => new(
            IsSuccess: isSuccess,
            SourceNode: node,
            SourceNodeResult: pathResult,
            NumberOfFilesAffected: node.IsDirectory ? 0 : 1,
            NumberOfFoldersAffected: node.IsDirectory ? 1 : 0,
            InputBytes: node.Size);
}
