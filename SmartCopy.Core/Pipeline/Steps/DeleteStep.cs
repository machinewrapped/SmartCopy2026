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

    internal static DeleteStep FromConfig(TransformStepConfig config) =>
        new(config.ParseEnum("deleteMode", DeleteMode.Trash));

    public StepKind StepType => StepKind.Delete;
    public bool IsExecutable => true;
    public bool IsConfigurable => false;
    public bool IsEditable => true;

    public string AutoSummary => Mode == DeleteMode.Permanent ? "Delete" : "Trash";
    public string Description => Mode == DeleteMode.Permanent ? "Delete permanently" : "Delete to Trash";

    public TransformStepConfig Config => new(StepType, new JsonObject { ["deleteMode"] = Mode.ToString() });

    public Task Validate(StepValidationContext context, CancellationToken ct = default)
    {
        context.ValidateHasSelectedInputs();
        context.ValidateSourceExists("Delete");
        context.NumFilterIncludedFiles   -= context.SelectedFileCount;
        context.TotalFilterIncludedBytes -= context.SelectedBytes;
        context.SelectedFileCount         = 0;
        context.SelectedBytes             = 0;
        context.SourceExists              = false;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        var pathResult = Mode == DeleteMode.Trash && context.SourceProvider.Capabilities.CanTrash
            ? SourceResult.Trashed
            : SourceResult.Deleted;

        // Include root node itself if selected, then all selected descendants.
        if (context.IsPreviewSelected(context.RootNode))
        {
            if (!context.AllowDeleteReadOnly && (context.RootNode.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                context.MarkFailed(context.RootNode);
                yield return MakePreviewResult(context.RootNode, SourceResult.Skipped, isSuccess: false);
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
                yield return MakePreviewResult(node, SourceResult.Skipped, isSuccess: false);
                continue;
            }

            yield return MakePreviewResult(node, pathResult);
        }
    }

    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        bool useTrash = Mode == DeleteMode.Trash
            && context.SourceProvider.Capabilities.CanTrash
            && context.TrashService.IsAvailable;

        // If the root node itself is fully selected, delete it atomically.
        if (context.RootNode.IsSelected)
        {
            if (!context.AllowDeleteReadOnly && (context.RootNode.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                context.MarkFailed(context.RootNode);
                yield return new TransformResult(
                    IsSuccess: false,
                    SourceNode: context.RootNode,
                    SourceNodeResult: SourceResult.Skipped,
                    NumberOfFilesSkipped: context.RootNode.CountAllFiles(),
                    NumberOfFoldersSkipped: context.RootNode.CountAllFolders(),
                    InputBytes: context.RootNode.Size);
                yield break;
            }

            SourceResult? rootActualResult = null;
            bool rootDeleted = false;
            try
            {
                rootActualResult = await DeleteNodeAsync(context.RootNode.FullPath, useTrash, context, ct);
                rootDeleted = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Atomic delete failed; fall back to per-node deletion below.
                context.MarkFailed(context.RootNode);
                _ = ex;
            }

            if (rootDeleted)
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: context.RootNode,
                    SourceNodeResult: rootActualResult!.Value,
                    NumberOfFilesAffected: context.RootNode.CountAllFiles(),
                    NumberOfFoldersAffected: context.RootNode.CountAllFolders(),
                    InputBytes: context.RootNode.Size);
                yield break;
            }
            // Atomic delete failed — fall through to per-node loop below.
        }

        foreach (var node in context.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (context.IsNodeFailed(node)) continue;
            // Skip nodes whose parent is also selected — the parent delete covers them.
            // Exception: if the parent's atomic delete already failed, process children individually.
            if (node.Parent?.IsSelected == true && !context.IsNodeFailed(node.Parent)) continue;

            if (!context.AllowDeleteReadOnly && (node.Attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                context.MarkFailed(node);
                yield return new TransformResult(
                    IsSuccess: false,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.Skipped,
                    NumberOfFilesSkipped: node.CountAllFiles(),
                    NumberOfFoldersSkipped: node.CountAllFolders(),
                    InputBytes: node.Size);
                continue;
            }

            SourceResult? actualResult = null;
            string? deleteError = null;
            try
            {
                actualResult = await DeleteNodeAsync(node.FullPath, useTrash, context, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                deleteError = ex.Message;
            }

            if (deleteError is not null)
            {
                context.MarkFailed(node);
                yield return new TransformResult(
                    IsSuccess: false,
                    SourceNode: node,
                    SourceNodeResult: SourceResult.Skipped,
                    NumberOfFilesSkipped: node.CountAllFiles(),
                    NumberOfFoldersSkipped: node.CountAllFolders(),
                    InputBytes: node.Size,
                    ErrorMessage: deleteError);
            }
            else
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourceNode: node,
                    SourceNodeResult: actualResult!.Value,
                    NumberOfFilesAffected: node.CountAllFiles(),
                    NumberOfFoldersAffected: node.CountAllFolders(),
                    InputBytes: node.Size);
            }
        }
    }

    private static async Task<SourceResult> DeleteNodeAsync(
        string fullPath, bool useTrash, IStepContext context, CancellationToken ct)
    {
        if (useTrash)
        {
            await context.TrashService.TrashAsync(fullPath, ct);
            return SourceResult.Trashed;
        }
        await context.SourceProvider.DeleteAsync(fullPath, ct);
        return SourceResult.Deleted;
    }

    private static TransformResult MakePreviewResult(
        DirectoryTreeNode node, SourceResult pathResult, bool isSuccess = true)
    {
        var isSkipped = pathResult == SourceResult.Skipped;
        return new TransformResult(
            IsSuccess: isSuccess,
            SourceNode: node,
            SourceNodeResult: pathResult,
            NumberOfFilesAffected: (node is DirectoryNode || isSkipped) ? 0 : 1,
            NumberOfFoldersAffected: (node is not DirectoryNode || isSkipped) ? 0 : 1,
            InputBytes: node.Size,
            NumberOfFilesSkipped: (node is DirectoryNode || !isSkipped) ? 0 : 1,
            NumberOfFoldersSkipped: (node is not DirectoryNode || !isSkipped) ? 0 : 1);
    }
}
