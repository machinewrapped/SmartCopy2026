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
        IStepContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        var pathResult = Mode == DeleteMode.Trash ? SourcePathResult.Trashed : SourcePathResult.Deleted;

        // Include root node itself if selected, then all selected descendants.
        if (ctx.RootNode.IsSelected)
        {
            yield return MakePreviewResult(ctx.RootNode, pathResult);
        }

        foreach (var node in ctx.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (ctx.IsNodeFailed(node)) continue;
            yield return MakePreviewResult(node, pathResult);
        }
    }

    public async IAsyncEnumerable<TransformResult> ApplyAsync(
        IStepContext ctx, [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var pathResult = Mode == DeleteMode.Trash ? SourcePathResult.Trashed : SourcePathResult.Deleted;

        // If the root node itself is fully selected, delete it atomically.
        if (ctx.RootNode.IsSelected)
        {
            await ctx.SourceProvider.DeleteAsync(ctx.RootNode.FullPath, ct);
            yield return new TransformResult(
                IsSuccess: true,
                SourcePath: ctx.RootNode.FullPath,
                SourcePathResult: pathResult,
                NumberOfFilesAffected: ctx.RootNode.CountAllFiles(),
                NumberOfFoldersAffected: ctx.RootNode.CountAllFolders(),
                InputBytes: ctx.RootNode.Size);
            yield break;
        }

        foreach (var node in ctx.RootNode.GetSelectedDescendants())
        {
            ct.ThrowIfCancellationRequested();
            if (ctx.IsNodeFailed(node)) continue;
            // Skip nodes whose parent is also selected — the parent delete covers them.
            if (node.Parent?.IsSelected == true) continue;

            await ctx.SourceProvider.DeleteAsync(node.FullPath, ct);
            yield return new TransformResult(
                IsSuccess: true,
                SourcePath: node.FullPath,
                SourcePathResult: pathResult,
                NumberOfFilesAffected: node.CountAllFiles(),
                NumberOfFoldersAffected: node.CountAllFolders(),
                InputBytes: node.Size);
        }
    }

    private static TransformResult MakePreviewResult(
        DirectoryTreeNode node, SourcePathResult pathResult)
        => new(
            IsSuccess: true,
            SourcePath: node.FullPath,
            SourcePathResult: pathResult,
            NumberOfFilesAffected: node.IsDirectory ? 0 : 1,
            NumberOfFoldersAffected: node.IsDirectory ? 1 : 0,
            InputBytes: node.Size);
}
