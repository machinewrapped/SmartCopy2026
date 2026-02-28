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

    public void Validate(StepValidationContext context)
    {
        context.ValidateHasSelectedInputs();
        context.ValidateSourceExists("Delete");
        // Post-condition: delete consumes the source.
        context.SourceExists = false;
    }

    public TransformStepConfig Config => new(
        StepType,
        new JsonObject
        {
            ["deleteMode"] = Mode.ToString(),
        });

    public async IAsyncEnumerable<TransformResult> PreviewAsync(TransformContext context, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        yield return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.SourceNode.FullPath,
            OutputBytes: context.SourceNode.Size,
            Message: "Delete preview",
            SourcePath: context.SourceNode.FullPath,
            Warning: PlanWarning.SourceWillBeRemoved);

        if (context.SourceNode.IsDirectory)
        {
            foreach (var child in GetSelectedDescendants(context.SourceNode))
            {
                yield return new TransformResult(
                    Success: true,
                    StepType: StepType,
                    DestinationPath: child.FullPath,
                    OutputBytes: child.Size,
                    Message: "Delete preview",
                    SourcePath: child.FullPath,
                    Warning: PlanWarning.SourceWillBeRemoved);
            }
        }
    }

    private static IEnumerable<FileSystemNode> GetSelectedDescendants(FileSystemNode node)
    {
        foreach (var file in node.Files)
            if (file.IsSelected) yield return file;
        foreach (var child in node.Children)
        {
            if (child.IsSelected) yield return child;
            foreach (var desc in GetSelectedDescendants(child))
                yield return desc;
        }
    }

    public async Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await context.SourceProvider.DeleteAsync(context.SourceNode.FullPath, ct);
        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.SourceNode.FullPath,
            OutputBytes: context.SourceNode.Size,
            Message: context.SourceNode.IsDirectory
                ? (Mode == DeleteMode.Trash ? "Directory deleted (trash)." : "Directory deleted permanently.")
                : (Mode == DeleteMode.Trash ? "Deleted (trash mode requested)." : "Deleted permanently."),
            SourcePath: context.SourceNode.FullPath);
    }
}
