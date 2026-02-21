using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class FlattenStep : ITransformStep
{
    public string StepType => "Flatten";
    public bool IsPathStep => true;
    public bool IsContentStep => false;
    public bool IsTerminal => false;

    public TransformStepConfig Config => new(StepType, new JsonObject());

    public TransformResult Preview(TransformContext context)
    {
        var flattened = Path.GetFileName(context.CurrentPath);
        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: flattened,
            Message: "Path flattened");
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        context.CurrentPath = Path.GetFileName(context.CurrentPath);
        return Task.FromResult(new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.CurrentPath,
            Message: "Path flattened"));
    }
}

