using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class DeleteStep : ITransformStep
{
    public string StepType => "Delete";
    public bool IsPathStep => false;
    public bool IsContentStep => false;
    public bool IsTerminal => true;

    public TransformStepConfig Config => new(
        StepType,
        new JsonObject());

    public TransformResult Preview(TransformContext context)
    {
        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.SourceNode.FullPath,
            Message: "Delete preview");
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
            Message: context.DeleteMode == DeleteMode.Trash
                ? "Deleted (trash mode requested)."
                : "Deleted permanently.");
    }
}

