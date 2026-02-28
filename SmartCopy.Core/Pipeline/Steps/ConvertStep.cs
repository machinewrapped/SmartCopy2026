using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using SmartCopy.Core.Pipeline.Validation;

namespace SmartCopy.Core.Pipeline.Steps;

public sealed class ConvertStep : ITransformStep
{
    public ConvertStep(string outputExtension)
    {
        OutputExtension = outputExtension ?? string.Empty;
    }

    public string OutputExtension { get; set; }

    public StepKind StepType => StepKind.Convert;
    public bool IsExecutable => false;

    public void Validate(StepValidationContext context)
    {
        context.ValidateSourceExists("Convert");
        // Post-condition: source is unchanged.
    }

    public TransformStepConfig Config => new(
        StepType,
        new JsonObject
        {
            ["outputExtension"] = OutputExtension,
        });

    public IEnumerable<TransformResult> Preview(TransformContext context)
    {
        if (context.SourceNode.IsDirectory)
            return [new TransformResult(Success: true, StepType: StepType, DestinationPath: null)];
        Apply(context);
        return [new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.DisplayPath,
            OutputBytes: context.SourceNode.Size,
            Message: "Convert preview")];
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (context.SourceNode.IsDirectory)
            return Task.FromResult(new TransformResult(Success: true, StepType: StepType, DestinationPath: null));
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
