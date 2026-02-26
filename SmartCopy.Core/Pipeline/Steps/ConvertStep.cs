using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class ConvertStep : ITransformStep
{
    public ConvertStep(string outputExtension)
    {
        OutputExtension = outputExtension ?? string.Empty;
    }

    public string OutputExtension { get; set; }

    public StepKind StepType => StepKind.Convert;
    public bool IsPathStep => false;
    public bool IsContentStep => true;
    public bool IsExecutable => false;

    public TransformStepConfig Config => new(
        StepType,
        new JsonObject
        {
            ["outputExtension"] = OutputExtension,
        });

    public TransformResult Preview(TransformContext context)
    {
        Apply(context);
        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.DisplayPath,
            OutputBytes: context.SourceNode.Size,
            Message: "Convert preview");
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Apply(context);
        return Task.FromResult(new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.DisplayPath,
            OutputBytes: context.SourceNode.Size,
            Message: "Converted"));
    }

    private void Apply(TransformContext context)
    {
        if (string.IsNullOrWhiteSpace(OutputExtension) || context.PathSegments.Length == 0)
            return;

        var extension = OutputExtension.Trim().TrimStart('.');
        var filename = context.PathSegments[^1];
        var convertedName = $"{Path.GetFileNameWithoutExtension(filename)}.{extension}";

        context.PathSegments = [.. context.PathSegments[..^1], convertedName];
        context.CurrentExtension = extension;
    }
}
