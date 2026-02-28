using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.FileSystem;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class DeleteStep : ITransformStep
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
        // Post-condition: delete consumes the source.
        context.SourceExists = false;
    }

    public async IAsyncEnumerable<TransformResult> PreviewAsync(TransformContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        var pathResult = Mode == DeleteMode.Trash ? SourcePathResult.Trashed : SourcePathResult.Deleted;

        yield return new TransformResult(
            IsSuccess: true,
            SourcePath: context.SourceNode.FullPath,
            SourcePathResult: pathResult,
            NumberOfFilesAffected: context.SourceNode.IsDirectory ? 0 : 1,
            NumberOfFoldersAffected: context.SourceNode.IsDirectory ? 1 : 0,
            InputBytes: context.SourceNode.Size);

        if (context.SourceNode.IsDirectory)
        {
            foreach (var child in context.SourceNode.GetSelectedDescendants())
            {
                yield return new TransformResult(
                    IsSuccess: true,
                    SourcePath: child.FullPath,
                    SourcePathResult: pathResult,
                    NumberOfFilesAffected: child.IsDirectory ? 0 : 1,
                    NumberOfFoldersAffected: child.IsDirectory ? 1 : 0,
                    InputBytes: child.Size);
            }
        }
    }

    public async Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await context.SourceProvider.DeleteAsync(context.SourceNode.FullPath, ct);
        return new TransformResult(
            IsSuccess: true,
            SourcePath: context.SourceNode.FullPath,
            SourcePathResult: Mode == DeleteMode.Trash ? SourcePathResult.Trashed : SourcePathResult.Deleted,
            NumberOfFilesAffected: CountAllFiles(context.SourceNode),
            NumberOfFoldersAffected: CountAllFolders(context.SourceNode),
            InputBytes: context.SourceNode.Size);
    }

    private static int CountAllFiles(FileSystemNode node) =>
        node.IsDirectory
            ? node.Files.Count + node.Children.Sum(CountAllFiles)
            : 1;

    private static int CountAllFolders(FileSystemNode node) =>
        node.IsDirectory
            ? 1 + node.Children.Sum(CountAllFolders)
            : 0;
}
