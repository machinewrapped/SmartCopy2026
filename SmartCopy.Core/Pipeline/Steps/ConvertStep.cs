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

    public string StepType => "Convert";
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
        var convertedPath = BuildConvertedPath(context.CurrentPath);
        return new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: convertedPath,
            OutputBytes: context.SourceNode.Size,
            Message: "Convert preview");
    }

    public Task<TransformResult> ApplyAsync(TransformContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        context.CurrentPath = BuildConvertedPath(context.CurrentPath);

        if (!string.IsNullOrWhiteSpace(OutputExtension))
        {
            context.CurrentExtension = OutputExtension.Trim().TrimStart('.');
        }

        return Task.FromResult(new TransformResult(
            Success: true,
            StepType: StepType,
            DestinationPath: context.CurrentPath,
            OutputBytes: context.SourceNode.Size,
            Message: "Converted"));
    }

    private string BuildConvertedPath(string currentPath)
    {
        if (string.IsNullOrWhiteSpace(OutputExtension))
        {
            return currentPath;
        }

        var extension = OutputExtension.Trim().TrimStart('.');
        var directory = Path.GetDirectoryName(currentPath);
        var fileName = Path.GetFileNameWithoutExtension(currentPath);
        var convertedName = $"{fileName}.{extension}";

        return string.IsNullOrWhiteSpace(directory)
            ? convertedName
            : Path.Combine(directory, convertedName);
    }
}
