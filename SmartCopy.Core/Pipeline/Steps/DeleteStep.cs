using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class DeleteStep : ITransformStep
{
    public DeleteStep(DeleteMode mode = DeleteMode.Trash)
    {
        Mode = mode;
    }

    public DeleteMode Mode { get; set; }

    public string StepType => "Delete";
    public bool IsPathStep => false;
    public bool IsContentStep => false;
    public bool IsExecutable => true;

    public TransformStepConfig Config => new(
        StepType,
        new JsonObject
        {
            ["deleteMode"] = Mode.ToString(),
        });

    public TransformResult Preview(TransformContext context)
    {
        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.SourceNode.FullPath,
            Message: "Delete preview",
            SourcePath: context.SourceNode.FullPath);
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
            Message: Mode == DeleteMode.Trash
                ? "Deleted (trash mode requested)."
                : "Deleted permanently.",
            SourcePath: context.SourceNode.FullPath);
    }
}
